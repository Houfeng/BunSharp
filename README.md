<div align="center">
  <img src="assets/BunSharp.png" width="200" alt="BunSharp" />

# BunSharp

[![NuGet](https://img.shields.io/nuget/v/BunSharp.svg)](https://www.nuget.org/packages/BunSharp)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com)
[![License: LGPL v2.1](https://img.shields.io/badge/License-LGPL_v2.1-blue.svg)](LICENSE)

</div>

BunSharp is a .NET binding for the [libbun](https://github.com/Houfeng/libbun) embed API (libbun based on [Bun](https://github.com/oven-sh/bun)). It lets you create a Bun runtime, execute JavaScript or TypeScript, and export C# types into the JS environment.

## Features

- Evaluate JavaScript and TypeScript from .NET
- Register host functions on the JS global object
- Export C# classes with `JSExportAttribute`
- Support instance methods, instance properties, selected static members, and dedicated `byte[]` binary marshalling via `Uint8Array` / `ArrayBuffer`
- Support `T[]` arrays as parameters and return values, including nested arrays (`T[][]`) and arrays of exported classes
- Support explicit persistent JS references via `JSObjectRef`, `JSFunctionRef`, `JSArrayRef`, `JSArrayBufferRef`, `JSTypedArrayRef`, and `JSBufferRef`
- No runtime reflection; AOT friendly

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

> **Note:** If analyzers are disabled without the explicit reference above, `JSExport` will not generate the required glue code and compilation will fail.

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
[JSExport]           // enable export
[JSExport(true)]     // same as above
[JSExport("name")]   // enable export and override the JS name
[JSExport(false)]    // disable export
```

- Apply `JSExport` to a class to export it
- Public members of an exported class are included by default if they use a supported export shape
- Class names stay unchanged by default
- Method and property names are exported as camelCase by default
- `JSExport(false)` excludes a member from export
- Only members that are actually exported participate in generator diagnostics

Compile-time restrictions:

- Static `JSObjectRef`, `JSFunctionRef`, `JSArrayRef`, `JSArrayBufferRef`, `JSTypedArrayRef`, and `JSBufferRef` properties and static method return values are rejected with `BSG010`
- Static delegate properties and static delegate method return values are rejected with `BSG011`

These diagnostics validate exported member shapes only. If application code stores runtime-affine values in its own global static state outside exported members, BunSharp cannot prove that code is safe across runtime lifetimes.

## Constructors

Exported classes can expose multiple JS-callable constructors, but BunSharp still publishes a single JavaScript `new Type(...)` entry point. The generator selects a constructor by JS-visible argument count.

- `public` constructors are JS-callable by default unless they are marked with `JSExport(false)`
- `internal` constructors are not JS-callable unless they are explicitly marked with `JSExport` or `JSExport(true)`
- If multiple JS-callable constructors have the same JS-visible argument count, compilation fails
- `BunContext` can be injected into exported constructors and instance methods; it does not count toward the JS-visible argument count
- `JSExport("name")` and `Stable = true` are not valid on constructors

Current scope:

- Constructor overload selection uses JS-visible argument count only
- Optional/default-value parameters and `params` constructors are not supported

## Arrays

`T[]` is supported wherever any other type is supported: constructor parameters, method parameters, return values, and properties. Supported element types are `bool`, `int`, `double`, `string`, `byte[]`, `BunValue`, any `[JSExport]` class, and nested arrays.

A JS `Array` maps to a C# `T[]`; `null` and `undefined` map to `null`.

```csharp
[JSExport]
public sealed class DataService
{
  public DataService() { }

  public string[] reverseNames(string[] names)
  {
    Array.Reverse(names);
    return names;
  }

  public DemoGreeter[] makeGreeters(string[] names)
    => names.Select(n => new DemoGreeter(n, [])).ToArray();

  public string[][] transpose(string[][] matrix) { /* ... */ }

  public static string[] Tags => ["fast", "aot", "ts"];
}
```

```ts
const svc = new DataService();
console.log(svc.reverseNames(["a", "b", "c"]));
console.log(svc.makeGreeters(["Alice", "Bob"])[0].describe());
console.log(DataService.tags);
```

> **Note:** `byte[]` does not use the general `T[]` mapper. JavaScript inputs must be `Uint8Array` or `ArrayBuffer`, managed `byte[]` values are exported as `Uint8Array`, and ordinary JS arrays are rejected for `byte[]` parameters and properties.

## Explicit Reference Types

`string`, `byte[]`, and `T[]` keep copy or snapshot semantics. Use explicit reference wrappers only when JS identity or shared backing storage must outlive the current call.

These wrappers use explicit ownership semantics. If your code creates, stores, or returns a `JSObjectRef`, `JSFunctionRef`, `JSArrayRef`, `JSArrayBufferRef`, `JSTypedArrayRef`, or `JSBufferRef`, it currently must call `Dispose()` explicitly when that reference is no longer needed. Runtime teardown and exported-instance cleanup are fallback release paths, not the normal ownership model.

- `JSObjectRef`: retain a JS object across calls and property writes
- `JSFunctionRef`: retain a JS function and call it later from C#
- `JSArrayRef`: retain a live JS `Array` with stable identity
- `JSArrayBufferRef`: retain a shared `ArrayBuffer`
- `JSTypedArrayRef`: retain a shared typed array and inspect its native layout
- `JSBufferRef`: retain a `Uint8Array` or Buffer-like byte view explicitly

Prefer keeping your domain model plain and isolating these wrappers in a small JS-facing bridge or facade.

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

Supported export shapes for these wrappers are constructor parameters, method parameters, instance properties, and instance method return values. Static properties and static method return values that use these wrappers are rejected with `BSG010`.

> **Note:** `BunValue` is still supported, but it should be treated as a temporary value channel. Use explicit reference wrappers only when the value must outlive the current call, preserve JS identity, or expose shared backing storage intentionally.

## Stable Identity

Keep the C# type plain and use `Stable = true` when you want stable JS identity for exported `byte[]` or `T[]` properties and method return values.

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

Repeated JS reads or method calls that observe the same managed array reference reuse the same JS `Array` or `Uint8Array`. If the source switches to another reference and later switches back, JS receives a new object.

`Stable` applies only to exported properties and method return values. It does not apply to constructors or parameters. If you need to retain an incoming plain `byte[]` or `T[]`, store it in your own state. If you need the original JS object identity or shared backing storage itself, use explicit reference wrappers instead of `Stable`.

Current implementation note: when ordinary C# code directly reassigns a `Stable` property, the existing JS-side cache is not cleared immediately. It is replaced on the next related JS read, or released during object or runtime cleanup.

> **Current scope:** `Stable` is supported on exported `byte[]` and `T[]` properties and method return values, plus delegate properties and delegate method return values where stable function-reference semantics are the default.

## Delegates

Exported instance delegate properties and instance delegate method return values are supported and use stable function-reference semantics.

```csharp
public delegate string MessageCallback(string message);

[JSExport]
public sealed class CallbackBridge
{
  public MessageCallback? Callback { get; set; }

  public MessageCallback GetCallback()
  {
    return message => $"default:{message}";
  }
}
```

Rules:

- Delegate properties default to stable behavior
- Delegate method return values default to stable behavior
- Explicit `Stable = true` is allowed
- Explicit `Stable = false` is rejected by the generator
- Static delegate properties and static delegate method return values are rejected with `BSG011`

When JS assigns a function to a delegate property, C# sees a typed delegate wrapper. When C# assigns a delegate or returns one from a method, JS sees a callable function, and repeated reads or returns reuse the same JS function object while the managed delegate reference stays the same.

Delegate parameters are not treated as stable exports. If you need to retain an incoming delegate, store it explicitly or use `JSFunctionRef` when the original JS function identity matters, and dispose that `JSFunctionRef` explicitly when you release it.

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
