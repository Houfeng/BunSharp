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

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "LBSG008");
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

    Assert.DoesNotContain(diagnostics, d => d.Id == "LBSG008");
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

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "LBSG001");
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

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "LBSG001");
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

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "LBSG001");
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

    var diagnostic = Assert.Single(diagnostics, d => d.Id == "LBSG009");
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

    Assert.DoesNotContain(diagnostics, d => d.Id == "LBSG009");
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