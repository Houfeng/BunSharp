using System.Reflection;
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
}