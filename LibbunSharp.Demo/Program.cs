using LibbunSharp;

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

	runtime.EvaluateOrThrow("globalThis.messageFromDotNet = helloFromDotNet('bun');");

	var result = context.GetProperty(global, "messageFromDotNet");
  var message = context.ToManagedString(result);
	Console.WriteLine(message);

	// while (runtime.RunPendingJobs())
	// {
	// }
  Console.WriteLine("Done.");
}
catch (Exception ex)
{
	Console.Error.WriteLine(ex.Message);
}
