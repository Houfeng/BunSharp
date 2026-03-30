# BunSharp

BunSharp is a scripting language for Bun. It is a scripting language that is designed to be easy to use and easy to learn. It is also designed to be fast and efficient.

## Features

- Easy to use
- Easy to learn
- Fast and efficient
- Supports functions
- Supports classes

## Installation

```bash
nuget install BunSharp
```

## Usage

Create a new BunSharp engine & execute a script

```csharp
using BunSharp;

var engine = BunRuntime.Create();
engine.Execute("print('Hello World!');");

```

Export a Native Class to TypeScript/JavaScript

```csharp

// [JSExport]
// class MyClass {
//     public string Name { get; set; }
// }

```
