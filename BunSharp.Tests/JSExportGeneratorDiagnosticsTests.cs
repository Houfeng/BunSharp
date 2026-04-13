using System.Collections.Immutable;
using System.Reflection;
using BunSharp.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BunSharp.Tests;

public sealed class JSExportGeneratorDiagnosticsTests
{
  [Fact]
  public void PrivateExplicitJsExportMember_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class PrivateMemberDemo
{
  public PrivateMemberDemo() { }

  [JSExport]
  private int hidden() => 1;
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG008");
    Assert.Contains("hidden", diagnostic.GetMessage());
    Assert.Contains("private", diagnostic.GetMessage());
  }

  [Fact]
  public void InternalExplicitJsExportMember_DoesNotReportAccessibilityDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class InternalMemberDemo
{
  public InternalMemberDemo() { }

  [JSExport]
  internal int echo() => 1;
}");

    Assert.DoesNotContain(diagnostics, d => d.Id == "BSG008");
  }

  [Fact]
  public void UnsupportedPropertyType_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using System;
using BunSharp;

[JSExport]
public sealed class UnsupportedPropertyDemo
{
  public UnsupportedPropertyDemo() { }

  public DateTime value { get; set; }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG001");
    Assert.Contains("value", diagnostic.GetMessage());
    Assert.Contains("System.DateTime", diagnostic.GetMessage());
  }

  [Fact]
  public void UnsupportedMethodReturnType_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using System;
using BunSharp;

[JSExport]
public sealed class UnsupportedReturnDemo
{
  public UnsupportedReturnDemo() { }

  public DateTime readValue() => default;
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG001");
    Assert.Contains("readValue", diagnostic.GetMessage());
    Assert.Contains("System.DateTime", diagnostic.GetMessage());
  }

  [Fact]
  public void UnsupportedMethodParameterType_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using System;
using BunSharp;

[JSExport]
public sealed class UnsupportedParameterDemo
{
  public UnsupportedParameterDemo() { }

  public void writeValue(DateTime value) { }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG001");
    Assert.Contains("writeValue", diagnostic.GetMessage());
    Assert.Contains("System.DateTime", diagnostic.GetMessage());
  }

  [Fact]
  public void PrivateNestedExportedClass_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

public sealed class Outer
{
  [JSExport]
  private sealed class Hidden
  {
    public Hidden() { }
  }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG009");
    Assert.Contains("Outer.Hidden", diagnostic.GetMessage());
    Assert.Contains("private", diagnostic.GetMessage());
  }

  [Fact]
  public void InternalTopLevelExportedClass_DoesNotReportTypeAccessibilityDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
internal sealed class InternalExportedType
{
  public InternalExportedType() { }
}");

    Assert.DoesNotContain(diagnostics, d => d.Id == "BSG009");
  }

  [Fact]
  public void StaticJsReferenceProperty_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class StaticJsReferencePropertyDemo
{
  public StaticJsReferencePropertyDemo() { }

  public static JSObjectRef? SharedObject { get; set; }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG010");
    Assert.Contains("SharedObject", diagnostic.GetMessage());
    Assert.Contains("JSObjectRef", diagnostic.GetMessage());
  }

  [Fact]
  public void StaticJsReferenceMethodReturn_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class StaticJsReferenceMethodDemo
{
  public StaticJsReferenceMethodDemo() { }

  public static JSFunctionRef? getSharedCallback() => null;
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG010");
    Assert.Contains("getSharedCallback", diagnostic.GetMessage());
    Assert.Contains("JSFunctionRef", diagnostic.GetMessage());
  }

  [Fact]
  public void PublicStaticDelegateProperty_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

public delegate string MessageCallback(string value);

[JSExport]
public sealed class StaticDelegatePropertyDemo
{
  public StaticDelegatePropertyDemo() { }

  public static MessageCallback? SharedCallback { get; set; }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG011");
    Assert.Contains("SharedCallback", diagnostic.GetMessage());
  }

  [Fact]
  public void InternalExplicitStaticDelegateProperty_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

public delegate string MessageCallback(string value);

[JSExport]
public sealed class ExplicitStaticDelegatePropertyDemo
{
  public ExplicitStaticDelegatePropertyDemo() { }

  [JSExport]
  internal static MessageCallback? SharedCallback { get; set; }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG011");
    Assert.Contains("SharedCallback", diagnostic.GetMessage());
  }

  [Fact]
  public void StaticDelegateMethodReturn_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

public delegate string MessageCallback(string value);

[JSExport]
public sealed class StaticDelegateMethodDemo
{
  public StaticDelegateMethodDemo() { }

  public static MessageCallback getSharedCallback() => value => value;
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG011");
    Assert.Contains("getSharedCallback", diagnostic.GetMessage());
  }

  [Fact]
  public void DisabledStaticDelegateProperty_DoesNotReportStaticDelegateDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

public delegate string MessageCallback(string value);

[JSExport]
public sealed class DisabledStaticDelegatePropertyDemo
{
  public DisabledStaticDelegatePropertyDemo() { }

  [JSExport(false)]
  public static MessageCallback? SharedCallback { get; set; }
}");

    Assert.DoesNotContain(diagnostics, d => d.Id == "BSG011");
  }

  [Fact]
  public void StaticJsReferenceParameter_DoesNotReportNewStaticDiagnostics()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class StaticJsReferenceParameterDemo
{
  public StaticJsReferenceParameterDemo() { }

  public static int count(JSObjectRef? value) => value is null ? 0 : 1;
}");

    Assert.DoesNotContain(diagnostics, d => d.Id == "BSG010");
    Assert.DoesNotContain(diagnostics, d => d.Id == "BSG011");
  }

  [Fact]
  public void ConflictingJsVisibleConstructorCounts_ReportCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class ConflictingConstructorDemo
{
  public ConflictingConstructorDemo() { }

  [JSExport]
  internal ConflictingConstructorDemo(BunContext context) { }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG012");
    Assert.Contains("ConflictingConstructorDemo", diagnostic.GetMessage());
    Assert.Contains("0", diagnostic.GetMessage());
  }

  [Fact]
  public void ConstructorNameOverride_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class ConstructorNameOverrideDemo
{
  [JSExport(""named"")]
  public ConstructorNameOverrideDemo() { }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG013");
    Assert.Contains("ConstructorNameOverrideDemo()", diagnostic.GetMessage());
    Assert.Contains("JSExport(\"name\")", diagnostic.GetMessage());
  }

  [Fact]
  public void ConstructorStableOption_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class ConstructorStableDemo
{
  [JSExport(Stable = true)]
  public ConstructorStableDemo() { }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG013");
    Assert.Contains("ConstructorStableDemo()", diagnostic.GetMessage());
    Assert.Contains("Stable = true", diagnostic.GetMessage());
  }

  [Fact]
  public void OptionalConstructorParameter_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class OptionalConstructorDemo
{
  public OptionalConstructorDemo(int value = 1) { }
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG015");
    Assert.Contains("OptionalConstructorDemo(int)", diagnostic.GetMessage());
    Assert.Contains("optional and default-value parameters", diagnostic.GetMessage());
  }

  [Fact]
  public void StaticMethodBunContextParameter_ReportsCompileTimeDiagnostic()
  {
    var diagnostics = RunGenerator(@"
using BunSharp;

[JSExport]
public sealed class StaticBunContextDemo
{
  public StaticBunContextDemo() { }

  public static int count(BunContext context) => 1;
}");

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "BSG014");
    Assert.Contains("count", diagnostic.GetMessage());
    Assert.Contains("BunContext", diagnostic.GetMessage());
  }

  private static ImmutableArray<Diagnostic> RunGenerator(string source)
  {
    var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
    var compilation = CSharpCompilation.Create(
      assemblyName: "GeneratorDiagnosticsTests",
      syntaxTrees: [syntaxTree],
      references: GetMetadataReferences(),
      options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    GeneratorDriver driver = CSharpGeneratorDriver.Create(new JSExportSourceGenerator());
    driver = driver.RunGenerators(compilation);

    return driver.GetRunResult().Results
      .SelectMany(static result => result.Diagnostics)
      .ToImmutableArray();
  }

  private static IEnumerable<MetadataReference> GetMetadataReferences()
  {
    var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
      ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
      ?? [];

    foreach (var assemblyPath in trustedPlatformAssemblies)
    {
      yield return MetadataReference.CreateFromFile(assemblyPath);
    }

    yield return MetadataReference.CreateFromFile(typeof(JSExportAttribute).Assembly.Location);
    yield return MetadataReference.CreateFromFile(typeof(JSExportSourceGenerator).GetTypeInfo().Assembly.Location);
  }
}