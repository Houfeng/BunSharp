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
  public static void Main()
  {

    try
    {
      using var runtime = BunRuntime.Create();
      var context = runtime.Context;
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

      // context.Evaluate("console.log('hello from bun');");
      // context.Evaluate("setTimeout(() => 1, 10);");

      context.Evaluate(@"
        console.time('loop1');
        let t = 0;
        for(let i=0; i<=1000000;i++){
          t+=i;
        }
        console.timeEnd('loop1');
     ");

      context.EvaluateFile(Path.GetFullPath("../../../assets/main.ts", AppDomain.CurrentDomain.BaseDirectory));
      var tValue = context.GetProperty(context.GlobalObject, "__t");
      var tString = context.ToManagedString(tValue);
      Console.WriteLine($"The type of Promise is: {tString}");

      RunBenchmark(context, "属性 set/get", "../../../assets/benchmarks/property-set-get.ts");
      RunBenchmark(context, "实例方法调用", "../../../assets/benchmarks/instance-method-call.ts");
      RunBenchmark(context, "字符串往返", "../../../assets/benchmarks/string-roundtrip.ts");
      RunBenchmark(context, "byte[] 往返", "../../../assets/benchmarks/byte-array-roundtrip.ts");

      while (runtime.RunPendingJobs())
      {
        Thread.Sleep(16);
      }
      Console.WriteLine("Done.");
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex.Message);
    }
  }

  private static void RunBenchmark(BunContext context, string name, string relativePath)
  {
    var path = Path.GetFullPath(relativePath, AppDomain.CurrentDomain.BaseDirectory);
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