using LibbunSharp;

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

      var result = context.EvaluateExpression("1+1");
      var message = context.ToManagedString(result);
      Console.WriteLine(message);

      var exportResult = context.EvaluateExpression(@"(() => {
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

      context.EvaluateFile("/Users/houfeng/Repositories/LibbunSharp/LibbunSharp.Demo/assets/main.ts");
      var tValue = context.GetProperty(context.GlobalObject, "__t");
      var tString = context.ToManagedString(tValue);
      Console.WriteLine($"The type of Promise is: {tString}");

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
}