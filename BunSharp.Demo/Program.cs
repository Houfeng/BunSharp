using BunSharp;

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