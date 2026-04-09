<div align="center">
  <img src="assets/BunSharp.png" width="120" alt="BunSharp" />

# BunSharp

[![NuGet](https://img.shields.io/nuget/v/BunSharp.svg)](https://www.nuget.org/packages/BunSharp)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

</div>

BunSharp is a .NET binding for the [libbun](https://github.com/Houfeng/libbun) embed API (libbun based on ([Bun](https://github.com/oven-sh/bun)). It lets you create a Bun runtime, execute JavaScript or TypeScript, and export C# types into the JS environment.
 
## Features

- Evaluate JavaScript and TypeScript from .NET
- Register host functions on the JS global object
- Export C# classes with `JSExportAttribute`
- Support instance methods, instance properties, static members, and `byte[]` ↔ `Uint8Array`
- Support `T[]` arrays as parameters and return values — including nested arrays (`T[][]`) and arrays of exported classes
- No runtime reflection — AOT friendly

## Requirements

- .NET 10.0 or later

| Platform | Architecture | Supported |
|----------|-------------|:---------:|
| Windows  | x64         | ✅        |
| Linux    | x64         | ✅        |
| macOS    | arm64       | ✅        |

## Installation

```bash
dotnet add package BunSharp
```

Or in your project file:

```xml
<ItemGroup>
  <PackageReference Include="BunSharp" Version="x.y.z" />
</ItemGroup>
```

`BunSharp` automatically pulls in `BunSharp.Generator` as a Roslyn analyzer. No additional setup is required for `JSExport` to work.

### Explicit Generator Configuration

If you need to pin the generator version independently from the main package, exclude the bundled analyzer and add `BunSharp.Generator` directly:

```xml
<ItemGroup>
  <PackageReference Include="BunSharp" Version="x.y.z" ExcludeAssets="analyzers" />
  <PackageReference Include="BunSharp.Generator" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

> **Note:** If analyzers are disabled (e.g. via `<EmitCompilerGeneratedFiles>false</EmitCompilerGeneratedFiles>`) without the explicit reference above, `JSExport` will not generate the necessary glue code and compilation will fail.

## Basic Usage

```csharp
using BunSharp;

using var runtime = BunRuntime.Create();
var context = runtime.Context;

context.Evaluate("globalThis.answer = 1 + 1;");

var result = context.GetProperty(context.GlobalObject, "answer");
Console.WriteLine(context.ToInt32(result));
```

## Export a C# Class to JavaScript

```csharp
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
[JSExport("name")]  // enable export and override the JS name
[JSExport(false)]   // disable export
```

- Apply `JSExport` to a class to export it
- Exported classes automatically include all public instance and static methods and properties
- Class names stay unchanged by default
- Method and property names are exported as camelCase by default
- `JSExport(false)` on a member excludes that member from export

## Arrays

`T[]` is supported wherever any other type is supported: constructor parameters, method parameters, return values, and properties. Supported element types: `bool`, `int`, `double`, `string`, `byte[]`, `BunValue`, any `[JSExport]` class, and nested arrays.

A JS `Array` is mapped to a C# `T[]`; `null` / `undefined` maps to `null`.

```csharp
[JSExport]
public sealed class DataService
{
    public DataService() { }

    // string[] ↔ JS Array of strings
    public string[] reverseNames(string[] names)
    {
        Array.Reverse(names);
        return names;
    }

    // [JSExport] class array
    public DemoGreeter[] makeGreeters(string[] names)
        => names.Select(n => new DemoGreeter(n, [])).ToArray();

    // nested array
    public string[][] transpose(string[][] matrix) { /* ... */ }

    // static property
    public static string[] Tags => ["fast", "aot", "ts"];
}
```

```ts
const svc = new DataService();
console.log(svc.reverseNames(["a", "b", "c"]));			// ["c", "b", "a"]
console.log(svc.makeGreeters(["Alice", "Bob"])[0].describe());	// Alice:0
console.log(DataService.tags);								// ["fast", "aot", "ts"]
```

> **Note:** `byte[]` always maps to `Uint8Array` via a zero-copy path and is independent of the general `T[]` mechanism.

## Host Functions

```csharp
using BunSharp;

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

## Contributing

Bug reports and pull requests are welcome on [GitHub](https://github.com/Houfeng/BunSharp). Please open an issue before submitting large changes.

## License

[MIT](LICENSE)
