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

public static class Program
{
  private const string DefaultDebuggerListenUrl = "tcp://127.0.0.1:6499";
  private const string DebuggerModeEnvironmentVariable = "BUNSHARP_DEMO_DEBUG_MODE";
  private const string DebuggerListenUrlEnvironmentVariable = "BUNSHARP_DEMO_DEBUG_URL";

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

    using var runtime = BunRuntime.Create(CreateScriptRuntimeOptions(scriptPath));

    var context = runtime.Context;
    ConfigureContext(context);
    context.EvaluateFile(scriptPath);
    DrainPendingJobs(runtime);

    Console.WriteLine("Done.");
  }

  private static BunRuntimeOptions CreateScriptRuntimeOptions(string scriptPath)
  {
    var debuggerMode = ResolveDebuggerMode();
    var debuggerListenUrl = ResolveDebuggerListenUrl(debuggerMode);

    return new BunRuntimeOptions
    {
      Cwd = Path.GetDirectoryName(scriptPath),
      DebuggerMode = debuggerMode,
      DebuggerListenUrl = debuggerListenUrl,
    };
  }

  private static BunDebuggerMode ResolveDebuggerMode()
  {
    var rawMode = Environment.GetEnvironmentVariable(DebuggerModeEnvironmentVariable);
    if (string.IsNullOrWhiteSpace(rawMode))
    {
      return BunDebuggerMode.Attach;
    }

    if (Enum.TryParse<BunDebuggerMode>(rawMode, ignoreCase: true, out var debuggerMode))
    {
      return debuggerMode;
    }

    throw new InvalidOperationException(
      $"Unsupported debugger mode '{rawMode}'. Expected one of: {string.Join(", ", Enum.GetNames<BunDebuggerMode>())}.");
  }

  private static string? ResolveDebuggerListenUrl(BunDebuggerMode debuggerMode)
  {
    if (debuggerMode == BunDebuggerMode.Off)
    {
      return null;
    }

    var rawUrl = Environment.GetEnvironmentVariable(DebuggerListenUrlEnvironmentVariable);
    return string.IsNullOrWhiteSpace(rawUrl) ? DefaultDebuggerListenUrl : rawUrl;
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

    RunBenchmarks(context);
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