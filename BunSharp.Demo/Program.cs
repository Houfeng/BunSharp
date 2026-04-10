using BunSharp;

public delegate string DemoCallback(string message);

[JSExport]
public sealed class DemoGreeter
{
  public DemoGreeter(string name, byte[] payload)
  {
    Name = name;
    Payload = payload;
  }

  public string Name { get; set; }

  public byte[] Payload { get; set; }

  public string describe()
  {
    return $"{Name}:{Payload.Length}";
  }

  public static string Version => "v1";

  [JSExport(false)]
  public string Hidden => "hidden";
}

[JSExport]
public sealed class BenchmarkBridge
{
  public BenchmarkBridge()
  {
  }

  public int Counter { get; set; }

  public string Text { get; set; } = string.Empty;

  public byte[] Buffer { get; set; } = [];

  public int add(int left, int right)
  {
    return left + right;
  }

  public string echoString(string value)
  {
    return value;
  }

  public byte[] echoBytes(byte[] value)
  {
    return value;
  }
}

[JSExport]
public sealed class ArrayDemo
{
  public ArrayDemo() { }

  public string[] reverseStrings(string[] items)
  {
    Array.Reverse(items);
    return items;
  }

  public int[] doubleInts(int[] values)
  {
    for (var i = 0; i < values.Length; i++)
      values[i] *= 2;
    return values;
  }

  public double sum(double[] values)
  {
    var total = 0.0;
    foreach (var v in values) total += v;
    return total;
  }

  public DemoGreeter[] makeGreeters(string[] names)
  {
    var result = new DemoGreeter[names.Length];
    for (var i = 0; i < names.Length; i++)
      result[i] = new DemoGreeter(names[i], new byte[] { (byte)i });
    return result;
  }

  public string[][] transpose(string[][] matrix)
  {
    if (matrix.Length == 0) return [];
    var rows = matrix.Length;
    var cols = matrix[0].Length;
    var result = new string[cols][];
    for (var c = 0; c < cols; c++)
    {
      result[c] = new string[rows];
      for (var r = 0; r < rows; r++)
        result[c][r] = matrix[r][c];
    }
    return result;
  }

  public static string[] StaticNames => ["alpha", "beta", "gamma"];
}

[JSExport]
public sealed class ReferenceDemo
{
  public ReferenceDemo()
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
public sealed class DelegatePropertyDemo
{
  public DelegatePropertyDemo()
  {
  }

  public DemoCallback? Callback { get; set; }

  [JSExport(Stable = true)]
  public DemoCallback? StableCallback { get; set; }

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
  private readonly DemoCallback _callbackA = value => $"left:{value}";
  private readonly DemoCallback _callbackB = value => $"right:{value}";
  private readonly DemoCallback _stableCallbackA = value => $"stable-left:{value}";
  private readonly DemoCallback _stableCallbackB = value => $"stable-right:{value}";

  public DelegateMethodDemo()
  {
  }

  public DemoCallback getCallback(bool alternate)
  {
    return alternate ? _callbackB : _callbackA;
  }

  [JSExport(Stable = true)]
  public DemoCallback getStableCallback(bool alternate)
  {
    return alternate ? _stableCallbackB : _stableCallbackA;
  }
}

[JSExport]
public sealed class ThrowingReferenceDemo
{
  private JSFunctionRef? _callback;

  public ThrowingReferenceDemo()
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
public sealed class IdentityOptionDemo
{
  private static readonly string[] StaticItemsA = ["left"];
  private static readonly string[] StaticItemsB = ["right"];
  private readonly string[] _itemsA = ["tag-a", "tag-b"];
  private readonly string[] _itemsB = ["swap-a", "swap-b"];
  private readonly byte[] _payloadA = [4, 5, 6];
  private readonly byte[] _payloadB = [7, 8, 9];

  public IdentityOptionDemo()
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
}

public static class Program
{

  public static void Main(string[] args)
  {
    try
    {
      if (args.Length > 0)
      {
        RunScriptMode(args[0]);
      }
      else
      {
        RunDefaultMode();
      }
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex.Message);
    }
  }

  private static void RunDefaultMode()
  {
    using var runtime = BunRuntime.Create();
    var context = runtime.Context;

    ConfigureContext(context);
    RunDefaultValidation(context);
    DrainPendingJobs(runtime);

    Console.WriteLine("Done.");
  }

  private static void RunScriptMode(string scriptPathArgument)
  {
    var scriptPath = ResolveScriptPath(scriptPathArgument);

    var options = CreateScriptRuntimeOptions(scriptPath);
    using var runtime = BunRuntime.Create(options);

    var context = runtime.Context;
    ConfigureContext(context);
    context.EvaluateFile(scriptPath);
    DrainPendingJobs(runtime);

    Console.WriteLine("Done.");
  }

  private static BunRuntimeOptions CreateScriptRuntimeOptions(string scriptPath)
  {
    return new BunRuntimeOptions
    {
      Cwd = Path.GetDirectoryName(scriptPath),
      DebuggerMode = BunDebuggerMode.Break,
      DebuggerListenUrl = "ws://127.0.0.1:6499/debug",
    };
  }

  private static void ConfigureContext(BunContext context)
  {
    var global = context.GlobalObject;

    var helloFunction = context.CreateFunction(
      "helloFromDotNet",
      static (ctx, args, _) =>
      {
        var name = args.Length > 0 ? ctx.ToManagedString(args[0]) : null;
        name ??= "world";

        Console.WriteLine($"C# callback invoked with '{name}'.");
        return ctx.CreateString($"Hello, {name}, from .NET.");
      },
      argCount: 1);

    if (!context.SetProperty(global, "helloFromDotNet", helloFunction))
    {
      throw new InvalidOperationException("Failed to register helloFromDotNet on the JS global object.");
    }

    context.ExportType<DemoGreeter>();
    context.ExportType<BenchmarkBridge>();
    context.ExportType<ArrayDemo>();
    context.ExportType<ReferenceDemo>();
    context.ExportType<DelegatePropertyDemo>();
    context.ExportType<DelegateMethodDemo>();
    context.ExportType<ThrowingReferenceDemo>();
    context.ExportType<IdentityOptionDemo>();
  }

  private static void RunDefaultValidation(BunContext context)
  {
    var result = context.Evaluate("1+1");
    var message = context.ToManagedString(result);
    Console.WriteLine(message);

    var result2 = context.Evaluate("String(1+1)");
    var message2 = context.ToManagedString(result2);
    Console.WriteLine(message2);

    var exportResult = context.Evaluate(@"(() => {
      const greeter = new DemoGreeter('Ada', new Uint8Array([1, 2, 3, 4]));
      return `${greeter.describe()}|${greeter.name}|${greeter.payload.length}|${DemoGreeter.version}`;
    })()");
    Console.WriteLine(context.ToManagedString(exportResult));

    context.Evaluate(@"
      console.time('loop1');
      let t = 0;
      for(let i=0; i<=1000000;i++){
        t+=i;
      }
      console.timeEnd('loop1');
   ");

    context.EvaluateFile(ResolveDemoAssetPath("main.ts"));
    var tValue = context.GetProperty(context.GlobalObject, "__t");
    var tString = context.ToManagedString(tValue);
    Console.WriteLine($"The type of Promise is: {tString}");

    RunArrayValidation(context);
    RunReferenceValidation(context);
    RunDelegatePropertyValidation(context);
    RunDelegateMethodValidation(context);
    RunReferenceDisposeValidation(context);
    RunReferenceExceptionValidation(context);
    RunStableIdentityOptionValidation(context);
    RunStableIdentityMethodValidation(context);
    RunBenchmarks(context);
  }

  private static void RunArrayValidation(BunContext context)
  {
    var arrayResult = context.Evaluate(@"(() => {
      const a = new ArrayDemo();
      const results = [];

      // string[] roundtrip
      const rev = a.reverseStrings(['a','b','c']);
      results.push('reverse: ' + rev.join(','));

      // int[] roundtrip
      const doubled = a.doubleInts([1,2,3]);
      results.push('double: ' + doubled.join(','));

      // double[] -> scalar
      results.push('sum: ' + a.sum([1.5, 2.5, 3.0]));

      // JSExport class array
      const greeters = a.makeGreeters(['Alice','Bob']);
      results.push('greeters: ' + greeters.map(g => g.describe()).join('; '));

      // nested string[][]
      const transposed = a.transpose([['a','b'],['c','d'],['e','f']]);
      results.push('transpose: ' + transposed.map(r => r.join(',')).join('|'));

      // static property string[]
      results.push('static: ' + ArrayDemo.staticNames.join(','));

      return results.join(' | ');
    })()");
    Console.WriteLine($"[Array validation] {context.ToManagedString(arrayResult)}");
  }

  private static void RunBenchmarks(BunContext context)
  {
    RunBenchmark(context, "属性 set/get", "property-set-get.ts");
    RunBenchmark(context, "实例方法调用", "instance-method-call.ts");
    RunBenchmark(context, "字符串往返", "string-roundtrip.ts");
    RunBenchmark(context, "byte[] 往返", "byte-array-roundtrip.ts");
  }

  private static void RunReferenceValidation(BunContext context)
  {
    var referenceResult = context.Evaluate(@"(() => {
      const demo = new ReferenceDemo();
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

    Console.WriteLine($"[Reference validation] {context.ToManagedString(referenceResult)}");
  }

  private static void RunDelegatePropertyValidation(BunContext context)
  {
    var result = context.Evaluate(@"(() => {
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

    Console.WriteLine($"[Delegate property] {context.ToManagedString(result)}");
  }

  private static void RunDelegateMethodValidation(BunContext context)
  {
    var result = context.Evaluate(@"(() => {
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

    Console.WriteLine($"[Delegate method] {context.ToManagedString(result)}");
  }

  private static void RunReferenceDisposeValidation(BunContext context)
  {
    var value = context.Evaluate(@"(() => {
      const demo = new ReferenceDemo();
      demo.callback = (message) => `dispose:${message}`;
      demo.items = ['dispose'];
      demo.buffer = new Uint8Array([99]);
      return demo;
    })()");

    var disposed = BunSharp.Generated.__JSExport_ReferenceDemo.DisposeExportedInstance(context, value);
    Console.WriteLine($"[Reference dispose] {disposed}");
  }

  private static void RunReferenceExceptionValidation(BunContext context)
  {
    var result = context.Evaluate(@"(() => {
      const demo = new ThrowingReferenceDemo();
      try {
        demo.callback = (message) => message;
        return `${demo.state()}|no-throw`;
      } catch (error) {
        return `${demo.state()}|throw`;
      }
    })()");

    Console.WriteLine($"[Reference exception] {context.ToManagedString(result)}");
  }

  private static void RunStableIdentityOptionValidation(BunContext context)
  {
    var result = context.Evaluate(@"(() => {
      const demo = new IdentityOptionDemo();

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

    Console.WriteLine($"[Stable identity option] {context.ToManagedString(result)}");
  }

  private static void RunStableIdentityMethodValidation(BunContext context)
  {
    var result = context.Evaluate(@"(() => {
      const demo = new IdentityOptionDemo();

      const itemsA1 = demo.getItems(false);
      const itemsA2 = demo.getItems(false);
      const itemsB = demo.getItems(true);
      const itemsA3 = demo.getItems(false);

      const payloadA1 = demo.getPayload(false);
      const payloadA2 = demo.getPayload(false);
      const payloadB = demo.getPayload(true);
      const payloadA3 = demo.getPayload(false);

      const staticA1 = IdentityOptionDemo.getStaticItems(false);
      const staticA2 = IdentityOptionDemo.getStaticItems(false);
      const staticB = IdentityOptionDemo.getStaticItems(true);
      const staticA3 = IdentityOptionDemo.getStaticItems(false);

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

    Console.WriteLine($"[Stable identity method] {context.ToManagedString(result)}");
  }

  private static void DrainPendingJobs(BunRuntime runtime)
  {
    while (runtime.RunPendingJobs())
    {
      Thread.Sleep(16);
    }
  }

  private static string ResolveScriptPath(string scriptPathArgument)
  {
    var scriptPath = Path.IsPathRooted(scriptPathArgument)
      ? scriptPathArgument
      : Path.GetFullPath(scriptPathArgument, Environment.CurrentDirectory);

    if (!File.Exists(scriptPath))
    {
      throw new FileNotFoundException($"Script file not found: {scriptPath}", scriptPath);
    }

    return scriptPath;
  }

  private static string ResolveDemoAssetPath(string fileName)
  {
    return Path.GetFullPath(Path.Combine("../../../assets", fileName), AppDomain.CurrentDomain.BaseDirectory);
  }

  private static void RunBenchmark(BunContext context, string name, string relativePath)
  {
    var path = ResolveDemoAssetPath(Path.Combine("benchmarks", relativePath));
    var script = File.ReadAllText(path);
    var resultValue = context.Evaluate($$"""
(() => {
  globalThis.__benchmarkResult = null;
{{script}}
  const value = globalThis.__benchmarkResult;
  return value == null ? null : String(value);
})()
""");

    if (context.IsUndefined(resultValue) || context.IsNull(resultValue))
    {
      throw new InvalidOperationException($"Benchmark '{name}' did not produce a result.");
    }

    Console.WriteLine($"[{name}] {context.ToManagedString(resultValue)}");
  }
}