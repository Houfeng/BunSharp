using System.Reflection;
using System.Threading;
using BunSharp;
using Xunit;

namespace BunSharp.Tests;

public sealed class RuntimeCleanupRegressionTests
{
  [Fact]
  public void JsObjectRef_Dispose_UnregistersPreDestroyCleanup()
  {
    using var runtime = BunRuntime.Create();
    var context = runtime.Context;
    var initialCount = GetCleanupCount(runtime, "_preDestroyCleanupCallbacks");

    var value = context.CreateObject();
    using (var objectRef = new JSObjectRef(context, value))
    {
      Assert.Equal(initialCount + 1, GetCleanupCount(runtime, "_preDestroyCleanupCallbacks"));
    }

    Assert.Equal(initialCount, GetCleanupCount(runtime, "_preDestroyCleanupCallbacks"));
  }

  [Fact]
  public void ExportRegistration_UsesPreDestroyCleanup_InsteadOfPostDestroyCleanup()
  {
    using var runtime = BunRuntime.Create();
    var context = runtime.Context;
    var initialPreDestroyCount = GetCleanupCount(runtime, "_preDestroyCleanupCallbacks");
    var initialPostDestroyCount = GetCleanupCount(runtime, "_cleanupCallbacks");

    context.ExportType<StableIdentityPropertyDemo>();

    Assert.Equal(initialPreDestroyCount + 1, GetCleanupCount(runtime, "_preDestroyCleanupCallbacks"));
    Assert.Equal(initialPostDestroyCount, GetCleanupCount(runtime, "_cleanupCallbacks"));
  }

  [Fact]
  public void JsObjectRef_Dispose_OnWrongThread_DoesNotDropCleanupRegistration()
  {
    using var runtime = BunRuntime.Create();
    var context = runtime.Context;
    var initialCount = GetCleanupCount(runtime, "_preDestroyCleanupCallbacks");
    var objectRef = new JSObjectRef(context, context.CreateObject());

    Exception? exception = null;
    var thread = new Thread(() => exception = Record.Exception(objectRef.Dispose));
    thread.Start();
    thread.Join();

    var invalidOperation = Assert.IsType<InvalidOperationException>(exception);
    Assert.Contains("initialized the runtime", invalidOperation.Message);
    Assert.Equal(initialCount + 1, GetCleanupCount(runtime, "_preDestroyCleanupCallbacks"));

    objectRef.Dispose();

    Assert.Equal(initialCount, GetCleanupCount(runtime, "_preDestroyCleanupCallbacks"));
  }

  [Fact]
  public void JsObjectRef_PreDestroyCleanup_DoesNotKeepWrapperInstanceAlive()
  {
    using var runtime = BunRuntime.Create();
    var context = runtime.Context;
    var initialCount = GetCleanupCount(runtime, "_preDestroyCleanupCallbacks");
    var weak = CreateAbandonedObjectRef(context);

    Assert.Equal(initialCount + 1, GetCleanupCount(runtime, "_preDestroyCleanupCallbacks"));

    ForceFullCollection();

    Assert.False(weak.TryGetTarget(out _));
    Assert.Equal(initialCount + 1, GetCleanupCount(runtime, "_preDestroyCleanupCallbacks"));
  }

  [Fact]
  public void BunRuntime_Dispose_OnWrongThread_Throws_And_RuntimeRemainsUsable()
  {
    var runtime = BunRuntime.Create();
    var context = runtime.Context;

    try
    {
      Exception? exception = null;
      var thread = new Thread(() => exception = Record.Exception(runtime.Dispose));
      thread.Start();
      thread.Join();

      var invalidOperation = Assert.IsType<InvalidOperationException>(exception);
      Assert.Contains("initialized the runtime", invalidOperation.Message);
      Assert.True(context.IsObject(context.CreateObject()));
    }
    finally
    {
      runtime.Dispose();
    }
  }

  [Fact]
  public void BunRuntime_Dispose_SurfacesCleanupCallbackFailures_AfterCleanup()
  {
    var runtime = BunRuntime.Create();
    var executed = false;
    runtime.Context.RegisterPreDestroyCleanup(() =>
    {
      executed = true;
      throw new InvalidOperationException("cleanup failed");
    });

    var exception = Assert.Throws<AggregateException>(runtime.Dispose);

    Assert.True(executed);
    var wrapped = Assert.Single(exception.InnerExceptions);
    Assert.Contains("pre-destroy", wrapped.Message);
    Assert.IsType<InvalidOperationException>(wrapped.InnerException);
    Assert.Equal("cleanup failed", wrapped.InnerException!.Message);
  }

  [Fact]
  public void BunRuntime_Dispose_AggregatesEventCallbackAndOwnedResourceFailures_AfterContinuingCleanup()
  {
    var runtime = BunRuntime.Create();
    var postDestroyExecuted = false;
    var eventCallbackDisposed = false;
    var throwingOwnedResource = new TrackingDisposable(() => throw new InvalidOperationException("owned resource failed"));
    var succeedingOwnedResource = new TrackingDisposable();

    runtime.Context.RegisterCleanup(() => postDestroyExecuted = true);
    runtime.SetEventCallback(static (_, _) => { });

    var eventCallbackHandle = GetPrivateField(runtime, "_eventCallbackHandle");
    Assert.NotNull(eventCallbackHandle);
    SetPrivateField(eventCallbackHandle!, "_removeFromOwner", (Action)(() =>
    {
      eventCallbackDisposed = true;
      throw new InvalidOperationException("event callback failed");
    }));

    AddOwnedResource(runtime, throwingOwnedResource);
    AddOwnedResource(runtime, succeedingOwnedResource);

    var exception = Assert.Throws<AggregateException>(runtime.Dispose);
    var messages = CollectExceptionMessages(exception);

    Assert.True(eventCallbackDisposed);
    Assert.True(postDestroyExecuted);
    Assert.True(throwingOwnedResource.Disposed);
    Assert.True(succeedingOwnedResource.Disposed);
    Assert.Contains(messages, message => message.Contains("event callback handle"));
    Assert.Contains(messages, message => message.Contains("event callback failed"));
    Assert.Contains(messages, message => message.Contains("owned resource"));
    Assert.Contains(messages, message => message.Contains("owned resource failed"));
    Assert.Throws<ObjectDisposedException>(() => _ = runtime.Context);
  }

  [Fact]
  public void BunRuntime_Dispose_AggregatesFinalizerRegistrationFailures_And_ContinuesOwnedResourceCleanup()
  {
    var runtime = BunRuntime.Create();
    var context = runtime.Context;
    var firstHandleDisposed = false;
    var secondHandleDisposed = false;
    var ownedResource = new TrackingDisposable();

    var registration = GetOrCreateObjectFinalizerRegistration(runtime, context, context.CreateObject());
    AddCallbackHandle(registration, CreateCallbackHandle(() =>
    {
      firstHandleDisposed = true;
      throw new InvalidOperationException("callback handle failed");
    }));
    AddCallbackHandle(registration, CreateCallbackHandle(() => secondHandleDisposed = true));
    AddOwnedResource(runtime, ownedResource);

    var exception = Assert.Throws<AggregateException>(runtime.Dispose);
    var messages = CollectExceptionMessages(exception);

    Assert.True(firstHandleDisposed);
    Assert.True(secondHandleDisposed);
    Assert.True(ownedResource.Disposed);
    Assert.Contains(messages, message => message.Contains("object finalizer registration"));
    Assert.Contains(messages, message => message.Contains("callback handle disposal failed"));
    Assert.Contains(messages, message => message.Contains("callback handle failed"));
  }

  private static int GetCleanupCount(BunRuntime runtime, string fieldName)
  {
    var field = typeof(BunRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.NotNull(field);

    var value = field!.GetValue(runtime);
    Assert.NotNull(value);

    var countProperty = value!.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
    Assert.NotNull(countProperty);
    return (int)countProperty!.GetValue(value)!;
  }

  private static WeakReference<JSObjectRef> CreateAbandonedObjectRef(BunContext context)
  {
    var objectRef = new JSObjectRef(context, context.CreateObject());
    return new WeakReference<JSObjectRef>(objectRef);
  }

  private static void AddOwnedResource(BunRuntime runtime, IDisposable resource)
  {
    var resources = GetPrivateField(runtime, "_ownedResources");
    Assert.NotNull(resources);

    var addMethod = resources!.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
    Assert.NotNull(addMethod);
    Assert.Equal(true, addMethod!.Invoke(resources, [resource]));
  }

  private static object GetOrCreateObjectFinalizerRegistration(BunRuntime runtime, BunContext context, BunValue target)
  {
    var method = typeof(BunRuntime).GetMethod("GetOrCreateObjectFinalizerRegistration", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.NotNull(method);

    var registration = method!.Invoke(runtime, [context, target]);
    Assert.NotNull(registration);
    return registration!;
  }

  private static void AddCallbackHandle(object registration, object handle)
  {
    var method = registration.GetType().GetMethod("AddCallbackHandle", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.NotNull(method);
    method!.Invoke(registration, [handle]);
  }

  private static object CreateCallbackHandle(Action onDispose)
  {
    var handleType = typeof(BunRuntime).Assembly.GetType("BunSharp.BunCallbackHandle", throwOnError: true);
    Assert.NotNull(handleType);

    var constructor = handleType!.GetConstructor(
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
      binder: null,
      [typeof(object)],
      modifiers: null);
    Assert.NotNull(constructor);

    var handle = constructor!.Invoke([new object()]);
    SetPrivateField(handle, "_removeFromOwner", onDispose);
    return handle;
  }

  private static object? GetPrivateField(object target, string fieldName)
  {
    var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.NotNull(field);
    return field!.GetValue(target);
  }

  private static void SetPrivateField(object target, string fieldName, object? value)
  {
    var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.NotNull(field);
    field!.SetValue(target, value);
  }

  private static List<string> CollectExceptionMessages(Exception exception)
  {
    var messages = new List<string>();
    CollectExceptionMessages(exception, messages);
    return messages;
  }

  private static void CollectExceptionMessages(Exception exception, List<string> messages)
  {
    messages.Add(exception.Message);

    if (exception is AggregateException aggregateException)
    {
      foreach (var inner in aggregateException.InnerExceptions)
      {
        CollectExceptionMessages(inner, messages);
      }

      return;
    }

    if (exception.InnerException is not null)
    {
      CollectExceptionMessages(exception.InnerException, messages);
    }
  }

  private static void ForceFullCollection()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
  }

  private sealed class TrackingDisposable : IDisposable
  {
    private readonly Action? _onDispose;

    public TrackingDisposable(Action? onDispose = null)
    {
      _onDispose = onDispose;
    }

    public bool Disposed { get; private set; }

    public void Dispose()
    {
      Disposed = true;
      _onDispose?.Invoke();
    }
  }
}