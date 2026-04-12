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

  private static void ForceFullCollection()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
  }
}