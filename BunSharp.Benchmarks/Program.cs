using BunSharp;

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
public sealed class StableCacheBenchmarkBridge
{
  private readonly string[] _itemsA = ["alpha", "beta", "gamma"];
  private readonly string[] _itemsB = ["delta", "epsilon", "zeta"];
  private bool _alternate;

  public StableCacheBenchmarkBridge()
  {
  }

  [JSExport(Stable = true)]
  public string[] Items => _alternate ? _itemsB : _itemsA;

  [JSExport(Stable = true)]
  public string[] getItems(bool alternate)
  {
    return alternate ? _itemsB : _itemsA;
  }

  public void setAlternate(bool alternate)
  {
    _alternate = alternate;
  }
}

public static class Program
{
  public static void Main()
  {
    using var runtime = BunRuntime.Create();
    var context = runtime.Context;

    context.ExportType<BenchmarkBridge>();
    context.ExportType<StableCacheBenchmarkBridge>();

    RunBenchmarks(context);
  }

  private static void RunBenchmarks(BunContext context)
  {
    RunBenchmark(context, "属性 set/get", "property-set-get.ts");
    RunBenchmark(context, "实例方法调用", "instance-method-call.ts");
    RunBenchmark(context, "字符串往返", "string-roundtrip.ts");
    RunBenchmark(context, "byte[] 往返", "byte-array-roundtrip.ts");
    RunBenchmark(context, "stable getter/method 热路径", "stable-cache-hot-path.ts");
  }

  private static string ResolveBenchmarkAssetPath(string fileName)
  {
    return Path.GetFullPath(Path.Combine("../../../benchmarks", fileName), AppDomain.CurrentDomain.BaseDirectory);
  }

  private static void RunBenchmark(BunContext context, string name, string relativePath)
  {
    var path = ResolveBenchmarkAssetPath(relativePath);
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