<div align="center">
  <img src="assets/BunSharp.png" width="200" alt="BunSharp" />

# BunSharp

[![NuGet](https://img.shields.io/nuget/v/BunSharp.svg)](https://www.nuget.org/packages/BunSharp)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com)
[![License: LGPL v2.1](https://img.shields.io/badge/License-LGPL_v2.1-blue.svg)](LICENSE)

</div>

BunSharp is a .NET binding for the [libbun](https://github.com/Houfeng/libbun) embed API (libbun based on ([Bun](https://github.com/oven-sh/bun)). It lets you create a Bun runtime, execute JavaScript or TypeScript, and export C# types into the JS environment.

## Features

- Evaluate JavaScript and TypeScript from .NET
- Register host functions on the JS global object
- Export C# classes with `JSExportAttribute`
- Support instance methods, instance properties, static members, and `byte[]` ↔ `Uint8Array`
- Support `T[]` arrays as parameters and return values — including nested arrays (`T[][]`) and arrays of exported classes
- Support explicit persistent JS references via `JSObjectRef`, `JSFunctionRef`, `JSArrayRef`, `JSArrayBufferRef`, `JSTypedArrayRef`, and `JSBufferRef`
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

var value = context.Evaluate(@"(() => {
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

## Explicit Reference Types

`string`, `byte[]`, and `T[]` keep copy or snapshot semantics. If you need stable JS identity or shared backing storage, use the explicit reference types instead of overloading the snapshot rules.

- `JSObjectRef`: retain a JS object across calls and property writes
- `JSFunctionRef`: retain a JS function and call it later from C#
- `JSArrayRef`: retain a live JS `Array` with stable identity
- `JSArrayBufferRef`: retain a shared `ArrayBuffer`
- `JSTypedArrayRef`: retain a shared typed array and inspect its native layout
- `JSBufferRef`: retain a `Uint8Array` or Buffer-like byte view explicitly

Ordinary `JSExport` classes do not need to expose these types. The default path is still to use plain C# types such as `string`, `byte[]`, `T[]`, and other `[JSExport]` classes.

These reference types are an advanced opt-in surface. Use them only when the JS-facing API must explicitly preserve JS identity or shared backing storage. `JSExport` supports them in constructor parameters, method parameters, return values, and properties so you can choose reference or shared semantics explicitly instead of relying on `BunValue` as an untyped escape hatch.

If a type is mainly part of your C# domain model and is also exported to JS, prefer keeping that model plain. When explicit JS reference semantics are needed, the better pattern is usually to add a thin JS-facing bridge or facade type that uses the reference wrappers only at the export boundary, instead of changing the core domain type to `JSObjectRef`, `JSFunctionRef`, `JSArrayRef`, or `JSArrayBufferRef` members.

For exported `byte[]` and `T[]` properties and method return values, there is also a middle ground: keep the C# type plain and opt into stable JS identity at the export boundary.

```csharp
[JSExport]
public sealed class IdentityOptionDemo
{
	[JSExport(Stable = true)]
	public string[] Items { get; set; } = ["a", "b"];

	[JSExport(Stable = true)]
	public byte[] Payload { get; set; } = [1, 2, 3];

	private readonly string[] _tags = ["fast", "stable"];

	[JSExport(Stable = true)]
	public string[] getTags()
	{
		return _tags;
	}
}
```

`Stable = true` enables stable JS identity for supported exports. Repeated JS reads of the same exported property return the same JS `Array` or `Uint8Array` object until the underlying C# property reference is replaced. Exported methods use the same rule for return values: consecutive returns of the same managed `byte[]` or `T[]` reference reuse the same cached JS object, but if the method switches to another reference and later switches back, JS receives a new object just like the property path.

`Stable` does not apply to constructor parameters or method parameters. Parameters are per-call inputs, and BunSharp does not try to infer whether an incoming value should be ignored after the call, retained as the latest value, or accumulated alongside other values. That decision belongs to your C# code.

If a method needs to keep an incoming plain `byte[]` or `T[]` beyond the current call, store it explicitly in your own state, such as a field or property. If that exported property uses `JSExport(Stable = true)`, later JS reads of that property will get stable JS identity for the stored managed value.

If you need to preserve the original JS object identity or shared backing storage itself, use the explicit reference types such as `JSFunctionRef`, `JSArrayRef`, `JSBufferRef`, or `JSArrayBufferRef` instead of relying on `Stable`.

> **Current scope:** `Stable` is currently supported only on exported `byte[]` and `T[]` properties and method return values, plus delegate properties and delegate method return values where stable function-reference semantics are the default. It is not used on constructors, parameters, or arbitrary member types.

## Delegate Properties

Exported delegate properties and delegate method return values are supported and always use stable function-reference semantics.

```csharp
public delegate string MessageCallback(string message);

[JSExport]
public sealed class CallbackBridge
{
	public MessageCallback? Callback { get; set; }

	[JSExport(Stable = true)]
	public MessageCallback? StableCallback { get; set; }

	public MessageCallback GetCallback()
	{
		return message => $"default:{message}";
	}

	[JSExport(Stable = true)]
	public MessageCallback GetStableCallback()
	{
		return message => $"stable:{message}";
	}
}
```

Rules:

- Delegate properties default to stable behavior even without `Stable = true`
- Delegate method return values also default to stable behavior even without `Stable = true`
- Explicit `Stable = true` is allowed
- Explicit `Stable = false` is rejected by the generator

When JS assigns a function to a delegate property, C# sees a typed delegate wrapper. When C# assigns a delegate to that property, JS reads back a callable function, and repeated reads return the same JS function object until the property changes.

When a method returns a delegate, JS receives a callable function. Repeated calls that return the same managed delegate reuse the same JS function object until that method returns a different delegate for the same exported member.

Like array and byte[] stable identity, this behavior is defined on exported properties and method return values only. Delegate parameters are not treated as stable exports; if a delegate passed into a call must be retained, store it explicitly in your own state or use `JSFunctionRef` for explicit JS-reference semantics.

Typical `JSExport` class: no explicit reference types needed.

```csharp
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
}
```

Advanced opt-in example: explicit reference or shared-memory semantics.

```csharp
[JSExport]
public sealed class BinaryBridge
{
	public BinaryBridge(JSFunctionRef onFlush)
	{
		OnFlush = onFlush;
	}

	public JSFunctionRef OnFlush { get; set; }

	public JSArrayRef? Children { get; set; }

	public JSArrayBufferRef? SharedBuffer { get; set; }
}
```

> **Note:** `BunValue` is still supported, but it should be treated as a temporary value channel. Use the explicit reference types above only when the value must outlive the current call, keep JS identity stable, or expose shared backing storage intentionally.

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

## Event Loop Integration

For GUI apps or custom host loops, call `RunPendingJobs()` from the thread that created the runtime. On macOS and Linux you can poll `EventFileDescriptor`; on Windows it returns `-1`.

For a cross-platform wake-up path, register `SetEventCallback()`. The callback runs on a Bun-managed background thread, so it should only signal your host loop and let the owning thread call `RunPendingJobs()` later.

```csharp
using BunSharp;

using var runtime = BunRuntime.Create();

runtime.SetEventCallback(static (_, _) =>
{
	// Wake your UI loop here, e.g. post to SynchronizationContext or enqueue work.
});

while (runtime.RunPendingJobs())
{
}
```

## Contributing

Bug reports and pull requests are welcome on [GitHub](https://github.com/Houfeng/BunSharp). Please open an issue before submitting large changes.

## License

[LGPL-2.1](LICENSE)
