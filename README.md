# LibbunSharp

LibbunSharp is a .NET binding for the Bun embed API. It lets you create a Bun runtime, execute JavaScript or TypeScript, and export C# types into the JS environment.

## Features

- Evaluate JavaScript and TypeScript from .NET
- Register host functions on the JS global object
- Export C# classes with `JSExportAttribute`
- Support instance methods, instance properties, static members, and `byte[] -> Uint8Array`

## Installation

```bash
dotnet add package LibbunSharp
```

The `JSExport` source generator is wired through the main package. Consumer projects do not need to reference `LibbunSharp.Generator` directly.

## Basic Usage

```csharp
using LibbunSharp;

using var runtime = BunRuntime.Create();
var context = runtime.Context;

context.Evaluate("globalThis.answer = 1 + 1;");

var result = context.GetProperty(context.GlobalObject, "answer");
Console.WriteLine(context.ToInt32(result));
```

## Export a C# Class to JavaScript

```csharp
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

using var runtime = BunRuntime.Create();
var context = runtime.Context;

context.ExportType<DemoGreeter>();

var value = context.EvaluateExpression(@"(() => {
	const greeter = new DemoGreeter('Ada', new Uint8Array([1, 2, 3, 4]));
	return `${greeter.describe()}|${greeter.name}|${greeter.payload.length}|${DemoGreeter.version}`;
})()");

Console.WriteLine(context.ToManagedString(value));
// Ada:4|Ada|4|v1
```

## JSExport Rules

```csharp
[JSExport]          // enable export
[JSExport(true)]    // same as above
[JSExport("name")] // enable export and override the JS name
[JSExport(false)]   // disable export
```

- Apply `JSExport` to a class to export it
- Exported classes automatically include all public instance and static methods and properties
- Class names stay unchanged by default
- Method and property names are exported as camelCase by default
- `JSExport(false)` on a member excludes that member from export

## Host Functions

```csharp
using LibbunSharp;

using var runtime = BunRuntime.Create();
var context = runtime.Context;

var hello = context.CreateFunction(
	"helloFromDotNet",
	static (ctx, args, _) =>
	{
		var name = args.Length > 0 ? ctx.ToManagedString(args[0]) : "world";
		return ctx.CreateString($"Hello, {name}, from .NET.");
	},
	argCount: 1);

context.SetProperty(context.GlobalObject, "helloFromDotNet", hello);
context.Evaluate("console.log(helloFromDotNet('Bun')); ");
```
