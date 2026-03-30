using LibbunSharp;

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

      var result = context.EvaluateExpression("1+1");
      var message = context.ToManagedString(result);
      Console.WriteLine(message);

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