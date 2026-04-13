using System.Reflection;
using BunSharp;
using BunSharp.Interop;
using Xunit;

namespace BunSharp.Tests;

public delegate string MessageCallback(string message);

[JSExport]
public sealed class StableIdentityPropertyDemo
{
  public StableIdentityPropertyDemo()
  {
  }

  [JSExport(Stable = true)]
  public string[] Items { get; set; } = ["a", "b"];

  [JSExport(Stable = true)]
  public byte[] Payload { get; set; } = [1, 2, 3];

  public void replaceItems(string[] items)
  {
    Items = items;
  }

  public void replacePayload(byte[] payload)
  {
    Payload = payload;
  }
}

[JSExport]
public sealed class StableIdentityMethodDemo
{
  private static readonly string[] StaticItemsA = ["left"];
  private static readonly string[] StaticItemsB = ["right"];

  private readonly string[] _itemsA = ["a", "1"];
  private readonly string[] _itemsB = ["b", "2"];
  private readonly byte[] _payloadA = [1, 2];
  private readonly byte[] _payloadB = [3, 4];

  public StableIdentityMethodDemo()
  {
  }

  [JSExport(Stable = true)]
  public string[] getItems(bool alternate)
  {
    return alternate ? _itemsB : _itemsA;
  }

  [JSExport(Stable = true)]
  public byte[] getPayload(bool alternate)
  {
    return alternate ? _payloadB : _payloadA;
  }

  [JSExport(Stable = true)]
  public static string[] getStaticItems(bool alternate)
  {
    return alternate ? StaticItemsB : StaticItemsA;
  }

  [JSExport(Stable = true)]
  public string[]? getNullableItems(int mode)
  {
    return mode switch
    {
      0 => _itemsA,
      1 => null,
      2 => _itemsB,
      _ => _itemsA,
    };
  }

  [JSExport(Stable = true)]
  public static string[]? getNullableStaticItems(int mode)
  {
    return mode switch
    {
      0 => StaticItemsA,
      1 => null,
      2 => StaticItemsB,
      _ => StaticItemsA,
    };
  }
}

[JSExport]
public sealed class ReferenceSemanticsDemo
{
  public ReferenceSemanticsDemo()
  {
  }

  public JSFunctionRef? Callback { get; set; }

  public JSArrayRef? Items { get; set; }

  public JSBufferRef? Buffer { get; set; }

  public JSArrayRef? rememberArray(JSArrayRef value)
  {
    Items = value;
    return Items;
  }

  public JSBufferRef? rememberBuffer(JSBufferRef value)
  {
    Buffer = value;
    return Buffer;
  }

  public string invokeStoredCallback(string message)
  {
    if (Callback is null)
    {
      return "missing";
    }

    Span<BunValue> args = stackalloc BunValue[1];
    args[0] = Callback.Context.CreateString(message);
    var result = Callback.Call(BunValue.Undefined, args);
    return Callback.Context.ToManagedString(result) ?? string.Empty;
  }

  public int storedArrayLength()
  {
    return checked((int)(Items?.Length ?? -1));
  }

  public int firstBufferByte()
  {
    if (Buffer is null || Buffer.ByteLength == 0)
    {
      return -1;
    }

    return Buffer.ToArray()[0];
  }
}

[JSExport]
public sealed class AutoDisposeExportedInstanceDemo : IDisposable
{
  private static int s_disposeCount;

  public AutoDisposeExportedInstanceDemo()
  {
  }

  public static int DisposeCount => s_disposeCount;

  public static void Reset()
  {
    s_disposeCount = 0;
  }

  public void Dispose()
  {
    s_disposeCount++;
  }
}

[JSExport]
public sealed class ImplicitConstructorDemo
{
  public string describe()
  {
    return "implicit";
  }
}

[JSExport]
public sealed class OverloadedConstructorDemo
{
  public OverloadedConstructorDemo()
  {
    Kind = "zero";
  }

  public OverloadedConstructorDemo(int value)
  {
    Kind = $"one:{value}";
  }

  public OverloadedConstructorDemo(int value, string label)
  {
    Kind = $"two:{value}:{label}";
  }

  public string Kind { get; }
}

[JSExport]
public sealed class ContextInjectedConstructorDemo
{
  private readonly nint _contextHandle;

  [JSExport(false)]
  public ContextInjectedConstructorDemo()
  {
    State = "hidden";
  }

  [JSExport]
  internal ContextInjectedConstructorDemo(BunContext context)
  {
    _contextHandle = context.Handle;
    State = context.IsUndefined(BunValue.Undefined) ? "ctx" : "bad";
  }

  public string State { get; }

  public string contextStatus(BunContext context, string suffix)
  {
    return _contextHandle == context.Handle ? $"{State}:{suffix}" : $"mismatch:{suffix}";
  }
}

[JSExport]
public sealed class ArrayMarshallingDemo
{
  private int[] _numbers = [];

  public ArrayMarshallingDemo()
  {
  }

  public int[] Numbers
  {
    get => _numbers;
    set => _numbers = value;
  }

  public int[] echoNumbers(int[] values)
  {
    return values;
  }

  public string[] echoStrings(string[] values)
  {
    return values;
  }

  public int[][] echoNestedNumbers(int[][] values)
  {
    return values;
  }

  public int[] createSequence(int count)
  {
    var result = new int[count];
    for (var i = 0; i < count; i++)
    {
      result[i] = checked((i * 2) + 1);
    }

    return result;
  }

  public int sumNumbers(int[] values)
  {
    var sum = 0;
    for (var i = 0; i < values.Length; i++)
    {
      sum += values[i];
    }

    return sum;
  }
}

[JSExport]
public sealed class ReferenceArrayMarshallingDemo
{
  public ReferenceArrayMarshallingDemo()
  {
  }

  public int countBuffers(JSBufferRef[] values)
  {
    return values.Length;
  }

  public int countNestedBuffers(JSBufferRef[][] values)
  {
    return values.Length;
  }
}

[JSExport]
public sealed class DelegatePropertyDemo
{
  public DelegatePropertyDemo()
  {
  }

  public MessageCallback? Callback { get; set; }

  [JSExport(Stable = true)]
  public MessageCallback? StableCallback { get; set; }

  public string invokeCallback(string value)
  {
    return Callback?.Invoke(value) ?? "missing";
  }

  public string invokeStableCallback(string value)
  {
    return StableCallback?.Invoke(value) ?? "missing";
  }

  public void setManagedCallback(string prefix)
  {
    Callback = value => $"{prefix}:{value}";
  }

  public void setManagedStableCallback(string prefix)
  {
    StableCallback = value => $"{prefix}:{value}";
  }
}

[JSExport]
public sealed class DelegateMethodDemo
{
  private readonly MessageCallback _callbackA = value => $"left:{value}";
  private readonly MessageCallback _callbackB = value => $"right:{value}";
  private readonly MessageCallback _stableCallbackA = value => $"stable-left:{value}";
  private readonly MessageCallback _stableCallbackB = value => $"stable-right:{value}";

  public DelegateMethodDemo()
  {
  }

  public MessageCallback getCallback(bool alternate)
  {
    return alternate ? _callbackB : _callbackA;
  }

  [JSExport(Stable = true)]
  public MessageCallback getStableCallback(bool alternate)
  {
    return alternate ? _stableCallbackB : _stableCallbackA;
  }
}

[JSExport]
public sealed class ThrowingReferenceSetterDemo
{
  private JSFunctionRef? _callback;

  public ThrowingReferenceSetterDemo()
  {
  }

  public JSFunctionRef? Callback
  {
    get => _callback;
    set
    {
      if (value is not null)
      {
        throw new InvalidOperationException("setter failed");
      }

      _callback = value;
    }
  }

  public string state()
  {
    return _callback is null ? "null" : "set";
  }
}

[JSExport]
public sealed class ThrowingCallbackSurfaceDemo
{
  private string _value = string.Empty;

  public ThrowingCallbackSurfaceDemo(bool fail)
  {
    if (fail)
    {
      throw new InvalidOperationException("constructor failed");
    }
  }

  public string BrokenProperty
  {
    get => throw new InvalidOperationException("getter failed");
  }

  public string WritableProperty
  {
    get => _value;
    set => throw new InvalidOperationException("instance setter failed");
  }

  public string boom()
  {
    throw new InvalidOperationException("method failed");
  }

  public static string BrokenStatic
  {
    get => throw new InvalidOperationException("static getter failed");
  }

  public static string WritableStatic
  {
    get => "ok";
    set => throw new InvalidOperationException("static setter failed");
  }
}

[JSExport]
public sealed class InternalExplicitMemberDemo
{
  public InternalExplicitMemberDemo()
  {
  }

  [JSExport]
  internal int Count { get; set; }

  internal int HiddenCount { get; set; }

  [JSExport]
  internal string echoInternal(string value)
  {
    return $"internal:{value}";
  }

  internal string hiddenEcho(string value)
  {
    return $"hidden:{value}";
  }
}

public static class NestedExportHost
{
  [JSExport]
  public sealed class NestedEchoDemo
  {
    public NestedEchoDemo()
    {
    }

    public string echo(string value)
    {
      return $"nested:{value}";
    }
  }
}

[JSExport]
public sealed class WrapperCacheChild
{
  public WrapperCacheChild(string name)
  {
    Name = name;
  }

  public string Name { get; }
}

[JSExport]
public sealed class WrapperCacheParent
{
  private readonly WrapperCacheChild _child = new("same");

  public WrapperCacheParent()
  {
  }

  public WrapperCacheChild Child => _child;

  public WrapperCacheChild getChild()
  {
    return _child;
  }

  public WrapperCacheChild[] getChildren()
  {
    return [_child, _child];
  }
}

public sealed class JSExportRegressionTests
{
  [Fact]
  public void Stable_OnPlainArrayProperties_ReusesJsObjectsUntilReferenceChanges()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new StableIdentityPropertyDemo();

      const items1 = demo.items;
      const items2 = demo.items;
      const payload1 = demo.payload;
      const payload2 = demo.payload;

      demo.replaceItems(['x', 'y']);
      const items3 = demo.items;

      demo.replacePayload(new Uint8Array([9, 8]));
      const payload3 = demo.payload;

      return [
        items1 === items2,
        payload1 === payload2,
        items2 !== items3,
        payload2 !== payload3,
        items3.join(','),
        Array.from(payload3).join(',')
      ].join('|');
    })()");

    Assert.Equal("true|true|true|true|x,y|9,8", env.Context.ToManagedString(result));
  }

  [Fact]
  public void ByteArrayMarshalling_UsesUint8ArrayAcceptsArrayBufferAndRejectsPlainArrays()
  {
    using var env = CreateEnvironment();

    var plainArray = env.Context.Evaluate("[1, 2, 3]");
    Assert.Throws<InvalidOperationException>(() => BunSharp.Generated.__JSExportCommon.ReadByteArray(env.Context, plainArray));

    var result = env.Context.Evaluate(@"(() => {
      const demo = new StableIdentityPropertyDemo();
      const initial = demo.payload;

      demo.replacePayload(new Uint8Array([7, 8, 9]).buffer);
      const fromBuffer = demo.payload;

      let plainArrayResult = 'no-throw';
      try {
        demo.replacePayload([1, 2, 3]);
      } catch {
        plainArrayResult = 'throw';
      }

      const afterPlainArray = demo.payload;

      return [
        initial instanceof Uint8Array,
        Array.isArray(initial),
        fromBuffer instanceof Uint8Array,
        Array.isArray(fromBuffer),
        Array.from(fromBuffer).join(','),
        plainArrayResult,
        afterPlainArray instanceof Uint8Array,
        Array.from(afterPlainArray).join(',')
      ].join('|');
    })()");

    Assert.Equal("true|false|true|false|7,8,9|throw|true|7,8,9", env.Context.ToManagedString(result));
  }

  [Fact]
  public void ByteArrayMarshalling_FallbackPath_UsesUint8ArrayAndPreservesPayload()
  {
    using var env = CreateEnvironment();

    var callbackContext = CreateUnownedContext(env.Context);
    var payload = Enumerable.Range(0, 513).Select(index => (byte)(index % 251)).ToArray();

    var value = BunSharp.Generated.__JSExportCommon.CreateByteArray(callbackContext, payload);

    Assert.False(env.Context.IsArray(value));
    Assert.True(env.Context.TryGetTypedArray(value, out var info));
    Assert.Equal(BunTypedArrayKind.Uint8Array, info.Kind);
    Assert.Equal((nuint)payload.Length, info.ElementCount);
    Assert.Equal(payload, new JSBufferRef(env.Context, value).ToArray());
  }

  [Fact]
  public void ByteArrayMarshalling_FastPath_RegistersRuntimeCleanupFallback()
  {
    using var env = CreateEnvironment();

    var initialCount = GetCleanupCount(env.Runtime, "_cleanupCallbacks");

    var value = BunSharp.Generated.__JSExportCommon.CreateByteArray(env.Context, [1, 2, 3, 4]);

    Assert.True(env.Context.TryGetTypedArray(value, out var info));
    Assert.Equal(BunTypedArrayKind.Uint8Array, info.Kind);
    Assert.Equal(initialCount + 1, GetCleanupCount(env.Runtime, "_cleanupCallbacks"));
  }

  [Fact]
  public void ReferenceArrayMarshalling_FailureDisposesPreviouslyConstructedNestedWrappers()
  {
    using var env = CreateEnvironment();

    var initialCount = GetCleanupCount(env.Runtime, "_preDestroyCleanupCallbacks");
    var value = env.Context.Evaluate("[[new Uint8Array([1])], [new Uint8Array([2]), new Uint32Array([3])]]");

    Assert.Throws<ArgumentException>(() => BunSharp.Generated.__JSExportCommon.ReadArray_Array_JSBufferRef(env.Context, value));
    Assert.Equal(initialCount, GetCleanupCount(env.Runtime, "_preDestroyCleanupCallbacks"));
  }

  [Fact]
  public void ExplicitReferenceMembers_PreserveExpectedReferenceAndSharedBufferSemantics()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new ReferenceSemanticsDemo();
      const callback = (message) => `callback:${message}`;
      demo.callback = callback;

      const callback2 = (message) => `callback2:${message}`;
      demo.callback = callback2;
      const replacedCallback = demo.invokeStoredCallback('swap');
      demo.callback = null;
      const clearedCallback = demo.invokeStoredCallback('missing');
      demo.callback = callback;

      const items = ['a', 'b', 'c'];
      const sameItems = demo.rememberArray(items);
      const items2 = ['z'];
      demo.items = items2;
      const replacedItems = demo.items === items2;
      demo.items = null;
      const clearedItems = demo.items === null;
      demo.items = items;

      const buffer = new Uint8Array([7, 8, 9]);
      demo.buffer = buffer;
      buffer[0] = 42;
      const buffer2 = new Uint8Array([5]);
      demo.buffer = buffer2;
      buffer2[0] = 11;
      const replacedBufferByte = demo.firstBufferByte();
      demo.buffer = null;
      const clearedBufferByte = demo.firstBufferByte();
      demo.buffer = buffer;

      return [
        sameItems === items,
        demo.items === items,
        replacedItems,
        clearedItems,
        demo.invokeStoredCallback('ok'),
        replacedCallback,
        clearedCallback,
        demo.storedArrayLength(),
        demo.rememberBuffer(buffer) === buffer,
        demo.firstBufferByte(),
        replacedBufferByte,
        clearedBufferByte
      ].join('|');
    })()");

    Assert.Equal("true|true|true|true|callback:ok|callback2:swap|missing|3|true|42|11|-1", env.Context.ToManagedString(result));
  }

  [Fact]
  public void PlainArrayMarshalling_PreservesSemanticsForLargeAndNestedArrays()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new ArrayMarshallingDemo();
      const numbers = Array.from({ length: 513 }, (_, index) => index - 256);
      const echoed = demo.echoNumbers(numbers);
      demo.numbers = numbers;
      const propertyValues = demo.numbers;
      const generated = demo.createSequence(513);
      const nested = demo.echoNestedNumbers([[1, 2], [3], []]);
      const strings = demo.echoStrings(['alpha', 'beta', 'gamma']);
      const empty = demo.echoNumbers([]);

      return [
        echoed.length,
        echoed[0],
        echoed[256],
        echoed[512],
        propertyValues[128],
        demo.sumNumbers(numbers),
        generated[0],
        generated[256],
        generated[512],
        nested.length,
        nested[0].join(','),
        nested[1].join(','),
        nested[2].length,
        strings.join(','),
        empty.length
      ].join('|');
    })()");

    Assert.Equal("513|-256|0|256|-128|0|1|513|1025|3|1,2|3|0|alpha,beta,gamma|0", env.Context.ToManagedString(result));
  }

  [Fact]
  public void DelegateProperties_DefaultToStableAndSupportJsAndManagedCallbacks()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new DelegatePropertyDemo();

      const jsCallback = (message) => `js:${message}`;
      demo.callback = jsCallback;
      const jsSame = demo.callback === jsCallback;
      const jsRepeat = demo.callback === demo.callback;
      const jsInvoke = demo.invokeCallback('one');

      demo.setManagedCallback('managed');
      const managed1 = demo.callback;
      const managed2 = demo.callback;
      const managedSame = managed1 === managed2;
      const managedInvoke = managed1('two');

      const jsStable = (message) => `stable:${message}`;
      demo.stableCallback = jsStable;
      const stableSame = demo.stableCallback === jsStable;
      const stableInvoke = demo.invokeStableCallback('three');

      demo.setManagedStableCallback('csharp');
      const managedStable1 = demo.stableCallback;
      const managedStable2 = demo.stableCallback;
      const managedStableSame = managedStable1 === managedStable2;
      const managedStableInvoke = managedStable1('four');

      return [
        jsSame,
        jsRepeat,
        jsInvoke,
        managedSame,
        managedInvoke,
        stableSame,
        stableInvoke,
        managedStableSame,
        managedStableInvoke
      ].join('|');
    })()");

    Assert.Equal("true|true|js:one|true|managed:two|true|stable:three|true|csharp:four", env.Context.ToManagedString(result));
  }

  [Fact]
  public void DelegateProperties_ReplacingStableJsCallbacks_DoesNotAccumulateOwnedReferences()
  {
    using var env = CreateEnvironment();

    var initialCount = GetCleanupCount(env.Runtime, "_preDestroyCleanupCallbacks");

    var result = env.Context.Evaluate(@"(() => {
      const demo = new DelegatePropertyDemo();
      const callbackA = (message) => `left:${message}`;
      const callbackB = (message) => `right:${message}`;

      demo.stableCallback = callbackA;
      const firstMatches = demo.stableCallback === callbackA;

      demo.stableCallback = callbackB;
      const secondMatches = demo.stableCallback === callbackB;
      const invokeAfterReplace = demo.invokeStableCallback('ok');

      demo.stableCallback = null;
      const cleared = demo.invokeStableCallback('missing');

      return [
        firstMatches,
        secondMatches,
        invokeAfterReplace,
        cleared,
        demo.stableCallback === null
      ].join('|');
    })()");

    Assert.Equal("true|true|right:ok|missing|true", env.Context.ToManagedString(result));
    Assert.Equal(initialCount, GetCleanupCount(env.Runtime, "_preDestroyCleanupCallbacks"));
  }

  [Fact]
  public void DelegateMethodReturns_DefaultToStableAndReuseJsFunctionsPerManagedDelegate()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new DelegateMethodDemo();

      const callback1 = demo.getCallback(false);
      const callback2 = demo.getCallback(false);
      const callbackAlt = demo.getCallback(true);
      const callback3 = demo.getCallback(false);

      const stable1 = demo.getStableCallback(false);
      const stable2 = demo.getStableCallback(false);
      const stableAlt = demo.getStableCallback(true);
      const stable3 = demo.getStableCallback(false);

      return [
        callback1 === callback2,
        callback1 !== callbackAlt,
        callback1 !== callback3,
        callback1('one'),
        callbackAlt('two'),
        stable1 === stable2,
        stable1 !== stableAlt,
        stable1 !== stable3,
        stable3('three')
      ].join('|');
    })()");

    Assert.Equal("true|true|true|left:one|right:two|true|true|true|stable-left:three", env.Context.ToManagedString(result));
  }

  [Fact]
  public void Stable_OnMethodReturnValues_ReusesJsObjectsPerReturnedManagedReference()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new StableIdentityMethodDemo();

      const itemsA1 = demo.getItems(false);
      const itemsA2 = demo.getItems(false);
      const itemsB = demo.getItems(true);
      const itemsA3 = demo.getItems(false);

      const payloadA1 = demo.getPayload(false);
      const payloadA2 = demo.getPayload(false);
      const payloadB = demo.getPayload(true);
      const payloadA3 = demo.getPayload(false);

      const staticA1 = StableIdentityMethodDemo.getStaticItems(false);
      const staticA2 = StableIdentityMethodDemo.getStaticItems(false);
      const staticB = StableIdentityMethodDemo.getStaticItems(true);
      const staticA3 = StableIdentityMethodDemo.getStaticItems(false);

      return [
        itemsA1 === itemsA2,
        itemsA1 !== itemsB,
        itemsA1 !== itemsA3,
        payloadA1 === payloadA2,
        payloadA1 !== payloadB,
        payloadA1 !== payloadA3,
        staticA1 === staticA2,
        staticA1 !== staticB,
        staticA1 !== staticA3,
        itemsA3.join(','),
        Array.from(payloadA3).join(','),
        staticA3.join(',')
      ].join('|');
    })()");

    Assert.Equal("true|true|true|true|true|true|true|true|true|a,1|1,2|left", env.Context.ToManagedString(result));
  }

  [Fact]
  public void Stable_OnMethodReturnValues_ClearsCachedIdentityWhenMethodReturnsNull()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new StableIdentityMethodDemo();

      const instanceA1 = demo.getNullableItems(0);
      const instanceNull = demo.getNullableItems(1);
      const instanceA2 = demo.getNullableItems(0);

      const staticA1 = StableIdentityMethodDemo.getNullableStaticItems(0);
      const staticNull = StableIdentityMethodDemo.getNullableStaticItems(1);
      const staticA2 = StableIdentityMethodDemo.getNullableStaticItems(0);

      return [
        instanceA1 !== null,
        instanceNull === null,
        instanceA2 !== null,
        instanceA1 !== instanceA2,
        instanceA2.join(','),
        staticA1 !== null,
        staticNull === null,
        staticA2 !== null,
        staticA1 !== staticA2,
        staticA2.join(',')
      ].join('|');
    })()");

    Assert.Equal("true|true|true|true|a,1|true|true|true|true|left", env.Context.ToManagedString(result));
  }

  [Fact]
  public void DisposingExportedInstance_ReleasesExplicitReferenceMembers()
  {
    using var env = CreateEnvironment();

    var value = env.Context.Evaluate(@"(() => {
      const demo = new ReferenceSemanticsDemo();
      demo.callback = (message) => `dispose:${message}`;
      demo.items = ['dispose'];
      demo.buffer = new Uint8Array([99]);
      return demo;
    })()");

    Assert.True(BunSharp.Generated.__JSExport_ReferenceSemanticsDemo.DisposeExportedInstance(env.Context, value));
  }

  [Fact]
  public void DisposingExportedInstance_CallsManagedDisposeExactlyOnce()
  {
    AutoDisposeExportedInstanceDemo.Reset();

    try
    {
      using var env = CreateEnvironment();

      var value = env.Context.Evaluate(@"(() => {
        const demo = new AutoDisposeExportedInstanceDemo();
        globalThis.disposeDemo = demo;
        return demo;
      })()");

      Assert.True(BunSharp.Generated.__JSExport_AutoDisposeExportedInstanceDemo.DisposeExportedInstance(env.Context, value));
      Assert.Equal(1, AutoDisposeExportedInstanceDemo.DisposeCount);
    }
    finally
    {
      Assert.Equal(1, AutoDisposeExportedInstanceDemo.DisposeCount);
      AutoDisposeExportedInstanceDemo.Reset();
    }
  }

  [Fact]
  public void RuntimePreDestroyCleanup_CallsManagedDisposeForTrackedInstances()
  {
    AutoDisposeExportedInstanceDemo.Reset();

    try
    {
      using (var env = CreateEnvironment())
      {
        env.Context.Evaluate(@"(() => {
          globalThis.trackedAutoDisposeDemo = new AutoDisposeExportedInstanceDemo();
        })()");

        Assert.Equal(0, AutoDisposeExportedInstanceDemo.DisposeCount);
      }

      Assert.Equal(1, AutoDisposeExportedInstanceDemo.DisposeCount);
    }
    finally
    {
      AutoDisposeExportedInstanceDemo.Reset();
    }
  }

  [Fact]
  public void ThrowingReferenceSetter_LeavesManagedStateUnchanged()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new ThrowingReferenceSetterDemo();
      try {
        demo.callback = (message) => message;
        return `${demo.state()}|no-throw`;
      } catch (error) {
        return `${demo.state()}|throw|${error instanceof Error}|${String(error.message).includes('setter failed')}`;
      }
    })()");

    Assert.Equal("null|throw|true|true", env.Context.ToManagedString(result));
  }

  [Fact]
  public void HostFunctionExceptions_AppearAsJsErrors()
  {
    using var env = CreateEnvironment();

    var boom = env.Context.CreateFunction("boom", (_, _, _) =>
    {
      throw new InvalidOperationException("host failed");
    });

    Assert.True(env.Context.SetProperty(env.Context.GlobalObject, "boom", boom));

    var result = env.Context.Evaluate(@"(() => {
      try {
        boom();
        return 'no-throw';
      } catch (error) {
        return [
          error instanceof Error,
          String(error.message).includes('host failed')
        ].join('|');
      }
    })()");

    Assert.Equal("true|true", env.Context.ToManagedString(result));
  }

  [Fact]
  public void ExportedInstanceMethodExceptions_AppearAsJsErrors()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new ThrowingCallbackSurfaceDemo(false);
      try {
        demo.boom();
        return 'no-throw';
      } catch (error) {
        return [
          error instanceof Error,
          String(error.message).includes('method failed')
        ].join('|');
      }
    })()");

    Assert.Equal("true|true", env.Context.ToManagedString(result));
  }

  [Fact]
  public void ExportedInstanceGetterExceptions_AppearAsJsErrors()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new ThrowingCallbackSurfaceDemo(false);
      try {
        return demo.brokenProperty;
      } catch (error) {
        return [
          error instanceof Error,
          String(error.message).includes('getter failed')
        ].join('|');
      }
    })()");

    Assert.Equal("true|true", env.Context.ToManagedString(result));
  }

  [Fact]
  public void ExportedConstructorExceptions_AppearAsJsErrors()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      try {
        new ThrowingCallbackSurfaceDemo(true);
        return 'no-throw';
      } catch (error) {
        return [
          error instanceof Error,
          String(error.message).includes('constructor failed')
        ].join('|');
      }
    })()");

    Assert.Equal("true|true", env.Context.ToManagedString(result));
  }

  [Fact]
  public void ExportedInstanceSetterExceptions_AppearAsJsErrors()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new ThrowingCallbackSurfaceDemo(false);
      try {
        demo.writableProperty = 'value';
        return 'no-throw';
      } catch (error) {
        return [
          error instanceof Error,
          String(error.message).includes('instance setter failed')
        ].join('|');
      }
    })()");

    Assert.Equal("true|true", env.Context.ToManagedString(result));
  }

  [Fact]
  public void ExportedStaticAccessorExceptions_AppearAsJsErrors()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      let getterResult = 'no-throw';
      let setterResult = 'no-throw';

      try {
        return ThrowingCallbackSurfaceDemo.brokenStatic;
      } catch (error) {
        getterResult = [
          error instanceof Error,
          String(error.message).includes('static getter failed')
        ].join('|');
      }

      try {
        ThrowingCallbackSurfaceDemo.writableStatic = 'value';
      } catch (error) {
        setterResult = [
          error instanceof Error,
          String(error.message).includes('static setter failed')
        ].join('|');
      }

      return `${getterResult}|${setterResult}`;
    })()");

    Assert.Equal("true|true|true|true", env.Context.ToManagedString(result));
  }

  [Fact]
  public void InternalMembers_RequireExplicitJsExportToBeIncluded()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new InternalExplicitMemberDemo();
      demo.count = 7;

      return [
        demo.count,
        demo.echoInternal('ok'),
        'hiddenCount' in demo,
        'hiddenEcho' in demo,
        typeof demo.hiddenEcho
      ].join('|');
    })()");

    Assert.Equal("7|internal:ok|false|false|undefined", env.Context.ToManagedString(result));
  }

  [Fact]
  public void NestedExportedClass_CanBeExportedAndUsed()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new NestedEchoDemo();
      return demo.echo('ok');
    })()");

    Assert.Equal("nested:ok", env.Context.ToManagedString(result));
  }

  [Fact]
  public void ManagedWrapperCache_ReusesJsWrappersForSameManagedInstance()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new WrapperCacheParent();
      const child1 = demo.child;
      const child2 = demo.child;
      const child3 = demo.getChild();
      const children = demo.getChildren();

      return [
        child1 === child2,
        child1 === child3,
        children[0] === children[1],
        children[0] === child1,
        child1.name
      ].join('|');
    })()");

    Assert.Equal("true|true|true|true|same", env.Context.ToManagedString(result));
  }

  [Fact]
  public void TryUnwrapExported_ReturnsManagedInstanceForMatchingExport()
  {
    using var env = CreateEnvironment();

    var value = env.Context.Evaluate(@"(() => {
      return new WrapperCacheChild('unwrap');
    })()");

    Assert.True(env.Context.TryUnwrapExported<WrapperCacheChild>(value, out var child));
    Assert.NotNull(child);
    Assert.Equal("unwrap", child.Name);
    Assert.False(env.Context.TryUnwrapExported<WrapperCacheParent>(value, out var parent));
    Assert.Null(parent);
  }

  [Fact]
  public void TryUnwrapExported_DoesNotRegisterTypeAsSideEffect()
  {
    using var runtime = BunRuntime.Create();
    var context = runtime.Context;

    Assert.True(context.IsUndefined(context.GetProperty(context.GlobalObject, "WrapperCacheChild")));

    var value = context.Evaluate("({})");

    Assert.False(context.TryUnwrapExported<WrapperCacheChild>(value, out var child));
    Assert.Null(child);
    Assert.True(context.IsUndefined(context.GetProperty(context.GlobalObject, "WrapperCacheChild")));
  }

  [Fact]
  public void TryGetExportedValue_ReturnsExistingWrapperForManagedInstance()
  {
    using var env = CreateEnvironment();

    var value = env.Context.Evaluate(@"(() => {
      const parent = new WrapperCacheParent();
      return parent.child;
    })()");

    Assert.True(env.Context.TryUnwrapExported<WrapperCacheChild>(value, out var child));
    Assert.NotNull(child);
    Assert.True(env.Context.TryGetExportedValue(child, out var existingValue));
    Assert.Equal(value, existingValue);

    var unmanagedChild = new WrapperCacheChild("fresh");
    Assert.False(env.Context.TryGetExportedValue(unmanagedChild, out var missingValue));
    Assert.Equal(default, missingValue);
  }

  [Fact]
  public void TryGetExportedValue_DoesNotRegisterTypeAsSideEffect()
  {
    using var runtime = BunRuntime.Create();
    var context = runtime.Context;
    var instance = new WrapperCacheChild("plain");

    Assert.True(context.IsUndefined(context.GetProperty(context.GlobalObject, "WrapperCacheChild")));

    Assert.False(context.TryGetExportedValue(instance, out var value));
    Assert.Equal(default, value);
    Assert.True(context.IsUndefined(context.GetProperty(context.GlobalObject, "WrapperCacheChild")));
  }

  [Fact]
  public void CompilerGeneratedDefaultConstructor_IsExported()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new ImplicitConstructorDemo();
      return demo.describe();
    })()");

    Assert.Equal("implicit", env.Context.ToManagedString(result));
  }

  [Fact]
  public void Constructors_DispatchByJsVisibleArgumentCount_AndRequireExactMatch()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const zero = new OverloadedConstructorDemo();
      const one = new OverloadedConstructorDemo(7);
      const two = new OverloadedConstructorDemo(7, 'x');

      let exact = 'no-throw';
      try {
        new OverloadedConstructorDemo(7, 'x', true);
      } catch (error) {
        exact = 'throw';
      }

      return [zero.kind, one.kind, two.kind, exact].join('|');
    })()");

    Assert.Equal("zero|one:7|two:7:x|throw", env.Context.ToManagedString(result));
  }

  [Fact]
  public void ConstructorAndInstanceMethod_BunContextIsInjected_AndPublicCtorCanBeExcluded()
  {
    using var env = CreateEnvironment();

    var result = env.Context.Evaluate(@"(() => {
      const demo = new ContextInjectedConstructorDemo();
      return [demo.state, demo.contextStatus('ok')].join('|');
    })()");

    Assert.Equal("ctx|ctx:ok", env.Context.ToManagedString(result));
  }

  private static TestEnvironment CreateEnvironment()
  {
    var env = new TestEnvironment();
    env.Context.ExportType<StableIdentityPropertyDemo>();
    env.Context.ExportType<StableIdentityMethodDemo>();
    env.Context.ExportType<ReferenceSemanticsDemo>();
    env.Context.ExportType<AutoDisposeExportedInstanceDemo>();
    env.Context.ExportType<ImplicitConstructorDemo>();
    env.Context.ExportType<OverloadedConstructorDemo>();
    env.Context.ExportType<ContextInjectedConstructorDemo>();
    env.Context.ExportType<ArrayMarshallingDemo>();
    env.Context.ExportType<ReferenceArrayMarshallingDemo>();
    env.Context.ExportType<DelegatePropertyDemo>();
    env.Context.ExportType<DelegateMethodDemo>();
    env.Context.ExportType<ThrowingReferenceSetterDemo>();
    env.Context.ExportType<ThrowingCallbackSurfaceDemo>();
    env.Context.ExportType<InternalExplicitMemberDemo>();
    env.Context.ExportType<NestedExportHost.NestedEchoDemo>();
    env.Context.ExportType<WrapperCacheChild>();
    env.Context.ExportType<WrapperCacheParent>();
    return env;
  }

  private static BunContext CreateUnownedContext(BunContext ownedContext)
  {
    var constructor = typeof(BunContext).GetConstructor(
      BindingFlags.Instance | BindingFlags.NonPublic,
      binder: null,
      [typeof(nint)],
      modifiers: null);

    Assert.NotNull(constructor);
    return (BunContext)constructor!.Invoke([ownedContext.Handle]);
  }

  private sealed class TestEnvironment : IDisposable
  {
    private readonly BunRuntime _runtime = BunRuntime.Create();

    public BunRuntime Runtime => _runtime;

    public BunContext Context => _runtime.Context;

    public void Dispose()
    {
      _runtime.Dispose();
    }
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