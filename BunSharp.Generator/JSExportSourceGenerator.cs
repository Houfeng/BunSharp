using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BunSharp.Generator;

[Generator]
public sealed class JSExportSourceGenerator : ISourceGenerator
{
    private const string JsExportAttributeMetadataName = "BunSharp.JSExportAttribute";
    private const string BunValueMetadataName = "BunSharp.BunValue";
    private const string BunContextMetadataName = "BunSharp.BunContext";
    private const string DiagnosticCategory = "BunSharp.JSExport";

    private static readonly DiagnosticDescriptor UnsupportedTypeDescriptor = new(
        id: "LBSG001",
        title: "Unsupported export type",
        messageFormat: "Member '{0}' uses unsupported type '{1}'. Supported types are bool, int, double, string, byte[], BunValue, void, and JS-exported classes in the same assembly.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MultipleConstructorsDescriptor = new(
        id: "LBSG002",
        title: "Unsupported constructor shape",
        messageFormat: "Type '{0}' must declare exactly one public instance constructor to be JS-exportable.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateExportNameDescriptor = new(
        id: "LBSG003",
        title: "Duplicate export name",
        messageFormat: "Type '{0}' has multiple exported {1} members named '{2}'.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidMemberContainerDescriptor = new(
        id: "LBSG004",
        title: "Member export requires exported class",
        messageFormat: "Member '{0}' cannot use JSExportAttribute because containing type '{1}' is not exported.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMemberShapeDescriptor = new(
        id: "LBSG005",
        title: "Unsupported export member",
        messageFormat: "Member '{0}' is not supported for JS export: {1}.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat FullyQualifiedFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var compilation = context.Compilation;
        var jsExportAttributeSymbol = compilation.GetTypeByMetadataName(JsExportAttributeMetadataName);
        var bunValueSymbol = compilation.GetTypeByMetadataName(BunValueMetadataName);
        var bunContextSymbol = compilation.GetTypeByMetadataName(BunContextMetadataName);

        if (jsExportAttributeSymbol is null || bunValueSymbol is null || bunContextSymbol is null)
        {
            return;
        }

        var classDeclarations = compilation.SyntaxTrees
            .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .ToArray();

        var candidateTypes = new List<INamedTypeSymbol>();
        foreach (var classDeclaration in classDeclarations)
        {
            var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol typeSymbol)
            {
                continue;
            }

            if (HasAttribute(typeSymbol, jsExportAttributeSymbol))
            {
                candidateTypes.Add(typeSymbol);
                continue;
            }

            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is not IMethodSymbol and not IPropertySymbol)
                {
                    continue;
                }

                if (HasAttribute(member, jsExportAttributeSymbol))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidMemberContainerDescriptor,
                        member.Locations.FirstOrDefault(),
                        member.Name,
                        typeSymbol.ToDisplayString()));
                }
            }
        }

        if (candidateTypes.Count == 0)
        {
            return;
        }

        var exportedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in candidateTypes)
        {
            if (!TryResolveExportRule(type, jsExportAttributeSymbol, out var rule) || !rule.Enabled)
            {
                continue;
            }

            exportedTypeNames.Add(type.ToDisplayString(FullyQualifiedFormat));
        }

        var models = new List<ExportedTypeModel>();
        foreach (var type in candidateTypes)
        {
            if (!TryBuildTypeModel(context, type, jsExportAttributeSymbol, bunValueSymbol, exportedTypeNames, out var model))
            {
                continue;
            }

            models.Add(model);
        }

        if (models.Count == 0)
        {
            return;
        }

        context.AddSource("BunSharp.JSExport.g.cs", GenerateSource(models));
    }

    private static bool TryBuildTypeModel(
        GeneratorExecutionContext context,
        INamedTypeSymbol type,
        INamedTypeSymbol jsExportAttributeSymbol,
        INamedTypeSymbol bunValueSymbol,
        ISet<string> exportedTypeNames,
        out ExportedTypeModel model)
    {
        model = default!;

        if (!TryResolveExportRule(type, jsExportAttributeSymbol, out var typeRule) || !typeRule.Enabled)
        {
            return false;
        }

        if (type.TypeKind != Microsoft.CodeAnalysis.TypeKind.Class || type.IsAbstract || type.IsStatic || type.IsGenericType)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedMemberShapeDescriptor,
                type.Locations.FirstOrDefault(),
                type.Name,
                "only non-abstract, non-static, non-generic classes are supported"));
            return false;
        }

        var constructors = type.InstanceConstructors
            .Where(static ctor => ctor.DeclaredAccessibility == Accessibility.Public && !ctor.IsImplicitlyDeclared)
            .ToArray();

        if (constructors.Length != 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MultipleConstructorsDescriptor,
                type.Locations.FirstOrDefault(),
                type.ToDisplayString()));
            return false;
        }

        var constructor = constructors[0];
        if (!TryCreateParameters(context, constructor.Parameters, constructor.Name, bunValueSymbol, exportedTypeNames, out var constructorParameters))
        {
            return false;
        }

        var instanceMethods = new List<ExportedMethodModel>();
        var staticMethods = new List<ExportedMethodModel>();
        var instanceProperties = new List<ExportedPropertyModel>();
        var staticProperties = new List<ExportedPropertyModel>();
        var instanceNames = new HashSet<string>(StringComparer.Ordinal);
        var staticNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    if (method.DeclaredAccessibility != Accessibility.Public)
                    {
                        continue;
                    }

                    if (!TryResolveMemberModel(context, type, method, jsExportAttributeSymbol, bunValueSymbol, exportedTypeNames, out var methodModel))
                    {
                        continue;
                    }

                    if (methodModel is null)
                    {
                        continue;
                    }

                    var methodNames = method.IsStatic ? staticNames : instanceNames;
                    var resolvedMethod = methodModel.Value;
                    if (!methodNames.Add(resolvedMethod.ExportName))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            DuplicateExportNameDescriptor,
                            method.Locations.FirstOrDefault(),
                            type.Name,
                            method.IsStatic ? "static" : "instance",
                            resolvedMethod.ExportName));
                        continue;
                    }

                    if (method.IsStatic)
                    {
                        staticMethods.Add(resolvedMethod);
                    }
                    else
                    {
                        instanceMethods.Add(resolvedMethod);
                    }

                    break;

                case IPropertySymbol property:
                    if (property.IsIndexer)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            UnsupportedMemberShapeDescriptor,
                            property.Locations.FirstOrDefault(),
                            property.Name,
                            "indexers are not supported"));
                        continue;
                    }

                    var hasPublicGetter = property.GetMethod?.DeclaredAccessibility == Accessibility.Public;
                    var hasPublicSetter = property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
                    if (!hasPublicGetter && !hasPublicSetter)
                    {
                        continue;
                    }

                    if (!TryResolvePropertyModel(context, type, property, jsExportAttributeSymbol, bunValueSymbol, exportedTypeNames, out var propertyModel))
                    {
                        continue;
                    }

                    if (propertyModel is null)
                    {
                        continue;
                    }

                    var propertyNames = property.IsStatic ? staticNames : instanceNames;
                    var resolvedProperty = propertyModel.Value;
                    if (!propertyNames.Add(resolvedProperty.ExportName))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            DuplicateExportNameDescriptor,
                            property.Locations.FirstOrDefault(),
                            type.Name,
                            property.IsStatic ? "static" : "instance",
                            resolvedProperty.ExportName));
                        continue;
                    }

                    if (property.IsStatic)
                    {
                        staticProperties.Add(resolvedProperty);
                    }
                    else
                    {
                        instanceProperties.Add(resolvedProperty);
                    }

                    break;
            }
        }

        model = new ExportedTypeModel(
            CreateTypeId(type),
            type.Name,
            typeRule.Name ?? type.Name,
            type.ToDisplayString(FullyQualifiedFormat),
            constructorParameters,
            instanceMethods,
            staticMethods,
            instanceProperties,
            staticProperties);

        return true;
    }

    private static bool TryResolveMemberModel(
        GeneratorExecutionContext context,
        INamedTypeSymbol containingType,
        IMethodSymbol method,
        INamedTypeSymbol jsExportAttributeSymbol,
        INamedTypeSymbol bunValueSymbol,
        ISet<string> exportedTypeNames,
        out ExportedMethodModel? model)
    {
        model = null;

        if (method.IsGenericMethod)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedMemberShapeDescriptor,
                method.Locations.FirstOrDefault(),
                method.Name,
                "generic methods are not supported"));
            return false;
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedMemberShapeDescriptor,
                method.Locations.FirstOrDefault(),
                method.Name,
                "ref returns are not supported"));
            return false;
        }

        if (!TryResolveExportRule(method, jsExportAttributeSymbol, out var exportRule))
        {
            return false;
        }

        if (!exportRule.Enabled)
        {
            return true;
        }

        if (!TryCreateParameters(context, method.Parameters, method.Name, bunValueSymbol, exportedTypeNames, out var parameters))
        {
            return false;
        }

        if (!TryCreateTypeRef(context, method.ReturnType, method.Name, bunValueSymbol, exportedTypeNames, allowVoid: true, out var returnType))
        {
            return false;
        }

        model = new ExportedMethodModel(
            method.Name,
            exportRule.Name ?? ToCamelCase(method.Name),
            method.IsStatic,
            parameters,
            returnType);

        return true;
    }

    private static bool TryResolvePropertyModel(
        GeneratorExecutionContext context,
        INamedTypeSymbol containingType,
        IPropertySymbol property,
        INamedTypeSymbol jsExportAttributeSymbol,
        INamedTypeSymbol bunValueSymbol,
        ISet<string> exportedTypeNames,
        out ExportedPropertyModel? model)
    {
        model = null;

        if (!TryResolveExportRule(property, jsExportAttributeSymbol, out var exportRule))
        {
            return false;
        }

        if (!exportRule.Enabled)
        {
            return true;
        }

        var hasGetter = property.GetMethod?.DeclaredAccessibility == Accessibility.Public;
        var hasSetter = property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
        if (!hasGetter && !hasSetter)
        {
            return true;
        }

        if (!TryCreateTypeRef(context, property.Type, property.Name, bunValueSymbol, exportedTypeNames, allowVoid: false, out var propertyType))
        {
            return false;
        }

        model = new ExportedPropertyModel(
            property.Name,
            exportRule.Name ?? ToCamelCase(property.Name),
            property.IsStatic,
            hasGetter,
            hasSetter,
            propertyType);

        return true;
    }

    private static bool TryCreateParameters(
        GeneratorExecutionContext context,
        ImmutableArray<IParameterSymbol> parameters,
        string memberName,
        INamedTypeSymbol bunValueSymbol,
        ISet<string> exportedTypeNames,
        out ImmutableArray<ParameterModel> result)
    {
        var builder = ImmutableArray.CreateBuilder<ParameterModel>(parameters.Length);
        foreach (var parameter in parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedMemberShapeDescriptor,
                    parameter.Locations.FirstOrDefault(),
                    memberName,
                    "ref, in, and out parameters are not supported"));
                result = default;
                return false;
            }

            if (!TryCreateTypeRef(context, parameter.Type, memberName, bunValueSymbol, exportedTypeNames, allowVoid: false, out var typeRef))
            {
                result = default;
                return false;
            }

            builder.Add(new ParameterModel(parameter.Name, typeRef));
        }

        result = builder.ToImmutable();
        return true;
    }

    private static bool TryCreateTypeRef(
        GeneratorExecutionContext context,
        ITypeSymbol type,
        string memberName,
        INamedTypeSymbol bunValueSymbol,
        ISet<string> exportedTypeNames,
        bool allowVoid,
        out TypeRefModel typeRef)
    {
        var fullyQualifiedName = type.ToDisplayString(FullyQualifiedFormat);
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                typeRef = new TypeRefModel(ExportValueKind.Bool, fullyQualifiedName, null, isNullableReferenceType: false);
                return true;
            case SpecialType.System_Int32:
                typeRef = new TypeRefModel(ExportValueKind.Int32, fullyQualifiedName, null, isNullableReferenceType: false);
                return true;
            case SpecialType.System_Double:
                typeRef = new TypeRefModel(ExportValueKind.Double, fullyQualifiedName, null, isNullableReferenceType: false);
                return true;
            case SpecialType.System_String:
                typeRef = new TypeRefModel(ExportValueKind.String, fullyQualifiedName, null, isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated);
                return true;
            case SpecialType.System_Void when allowVoid:
                typeRef = new TypeRefModel(ExportValueKind.Void, fullyQualifiedName, null, isNullableReferenceType: false);
                return true;
        }

        if (type is IArrayTypeSymbol arrayType && arrayType.Rank == 1 && arrayType.ElementType.SpecialType == SpecialType.System_Byte)
        {
            typeRef = new TypeRefModel(ExportValueKind.ByteArray, fullyQualifiedName, null, isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated);
            return true;
        }

        if (SymbolEqualityComparer.Default.Equals(type, bunValueSymbol))
        {
            typeRef = new TypeRefModel(ExportValueKind.BunValue, fullyQualifiedName, null, isNullableReferenceType: false);
            return true;
        }

        if (type is INamedTypeSymbol namedType)
        {
            var exportTypeName = namedType.ToDisplayString(FullyQualifiedFormat);
            if (exportedTypeNames.Contains(exportTypeName))
            {
                typeRef = new TypeRefModel(ExportValueKind.ExportedObject, exportTypeName, CreateTypeId(namedType), isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated);
                return true;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            UnsupportedTypeDescriptor,
            type.Locations.FirstOrDefault(),
            memberName,
            type.ToDisplayString()));

        typeRef = default;
        return false;
    }

    private static bool TryResolveExportRule(ISymbol symbol, INamedTypeSymbol jsExportAttributeSymbol, out ExportRule rule)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, jsExportAttributeSymbol))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 0)
            {
                rule = new ExportRule(true, null);
                return true;
            }

            var argument = attribute.ConstructorArguments[0];
            if (argument.Kind == TypedConstantKind.Primitive && argument.Type?.SpecialType == SpecialType.System_Boolean)
            {
                rule = new ExportRule(argument.Value is true, null);
                return true;
            }

            if (argument.Kind == TypedConstantKind.Primitive && argument.Type?.SpecialType == SpecialType.System_String)
            {
                rule = new ExportRule(true, (string?)argument.Value);
                return true;
            }
        }

        rule = new ExportRule(true, null);
        return !HasAttribute(symbol, jsExportAttributeSymbol) || rule.Enabled;
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeSymbol)
    {
        return symbol.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol));
    }

    private static string GenerateSource(IReadOnlyList<ExportedTypeModel> models)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace BunSharp.Generated;");
        builder.AppendLine();
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Runtime.InteropServices;");
        builder.AppendLine();
        builder.AppendLine("internal static class __BunGeneratedExportsModuleInitializer");
        builder.AppendLine("{");
        builder.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        builder.AppendLine("    internal static void Initialize()");
        builder.AppendLine("    {");
        foreach (var model in models)
        {
            builder.Append("        global::BunSharp.JSExportRegistry.RegisterExport(typeof(");
            builder.Append(model.FullyQualifiedTypeName);
            builder.Append("), context => { ");
            builder.Append(model.Id);
            builder.AppendLine(".Register(context); return true; });");
        }
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
        AppendCommonHelpers(builder);

        foreach (var model in models)
        {
            AppendTypeModel(builder, model);
        }

        return builder.ToString();
    }

    private static void AppendCommonHelpers(StringBuilder builder)
    {
        builder.AppendLine("internal static class __JSExportCommon");
        builder.AppendLine("{");
        builder.AppendLine("    public static void EnsurePropertySet(bool result, string name)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!result)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new InvalidOperationException($\"Failed to set JS property '{name}'.\");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static void EnsureArgumentCount(string name, global::System.ReadOnlySpan<global::BunSharp.BunValue> args, int expected)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (args.Length < expected)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new InvalidOperationException($\"JS export '{name}' expected at least {expected} argument(s) but received {args.Length}.\");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static string? ReadString(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return context.ToManagedString(value);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static byte[]? ReadByteArray(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (context.TryGetTypedArray(value, out var typedArray))");
        builder.AppendLine("        {");
        builder.AppendLine("            if (typedArray.Kind != global::BunSharp.Interop.BunTypedArrayKind.Uint8Array)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new InvalidOperationException(\"Expected Uint8Array for byte[] export.\");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var result = new byte[checked((int)typedArray.ByteLength)];");
        builder.AppendLine("            if (result.Length == 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                return result;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            Marshal.Copy(IntPtr.Add(typedArray.Data, checked((int)typedArray.ByteOffset)), result, 0, result.Length);");
        builder.AppendLine("            return result;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (context.TryGetArrayBuffer(value, out var arrayBuffer))");
        builder.AppendLine("        {");
        builder.AppendLine("            var result = new byte[checked((int)arrayBuffer.ByteLength)];");
        builder.AppendLine("            if (result.Length == 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                return result;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            Marshal.Copy(arrayBuffer.Data, result, 0, result.Length);");
        builder.AppendLine("            return result;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        throw new InvalidOperationException(\"Expected Uint8Array or ArrayBuffer for byte[] export.\");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static void ReleaseUnmanagedBuffer(nint userdata)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (userdata != 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            Marshal.FreeHGlobal(userdata);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.BunValue CreateByteArray(global::BunSharp.BunContext context, byte[]? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (value is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            return global::BunSharp.BunValue.Null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (value.Length == 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            return context.CreateTypedArray(global::BunSharp.Interop.BunTypedArrayKind.Uint8Array, 0, 0);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var buffer = Marshal.AllocHGlobal(value.Length);");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            Marshal.Copy(value, 0, buffer, value.Length);");
        builder.AppendLine("            if (context.CanRetainManagedResources)");
        builder.AppendLine("            {");
        builder.AppendLine("                return context.CreateTypedArray(global::BunSharp.Interop.BunTypedArrayKind.Uint8Array, buffer, checked((nuint)value.Length), ReleaseUnmanagedBuffer, buffer);");
        builder.AppendLine("            }");
        builder.AppendLine("            Marshal.FreeHGlobal(buffer);");
        builder.AppendLine("            buffer = 0;");
        builder.AppendLine("            var array = context.CreateArray(checked((nuint)value.Length));");
        builder.AppendLine("            for (var index = 0; index < value.Length; index++)");
        builder.AppendLine("            {");
        builder.AppendLine("                EnsurePropertySet(context.SetIndex(array, (uint)index, context.CreateInt32(value[index])), index.ToString(global::System.Globalization.CultureInfo.InvariantCulture));");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var constructor = context.GetProperty(context.GlobalObject, \"Uint8Array\");");
        builder.AppendLine("            var from = context.GetProperty(constructor, \"from\");");
        builder.AppendLine("            global::BunSharp.BunValue[] args = [array];");
        builder.AppendLine("            return context.Call(from, constructor, args);");
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            if (buffer != 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                Marshal.FreeHGlobal(buffer);");
        builder.AppendLine("            }");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendTypeModel(StringBuilder builder, ExportedTypeModel model)
    {
        builder.Append("internal static class ");
        builder.Append(model.Id);
        builder.AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    private sealed class RegistrationState");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly HashSet<nint> _trackedHandles = new();");
        builder.AppendLine();
        builder.AppendLine("        public RegistrationState(global::BunSharp.BunClass @class, global::BunSharp.BunValue constructor)");
        builder.AppendLine("        {");
        builder.AppendLine("            Class = @class;");
        builder.AppendLine("            Constructor = constructor;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public global::BunSharp.BunClass Class { get; }");
        builder.AppendLine();
        builder.AppendLine("        public global::BunSharp.BunValue Constructor { get; }");
        builder.AppendLine();
        builder.AppendLine("        public void TrackHandle(nint handle)");
        builder.AppendLine("        {");
        builder.AppendLine("            lock (_trackedHandles) _trackedHandles.Add(handle);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public bool UntrackHandle(nint handle)");
        builder.AppendLine("        {");
        builder.AppendLine("            lock (_trackedHandles) return _trackedHandles.Remove(handle);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void ReleaseTrackedHandles()");
        builder.AppendLine("        {");
        builder.AppendLine("            lock (_trackedHandles)");
        builder.AppendLine("            {");
        builder.AppendLine("                foreach (var h in _trackedHandles)");
        builder.AppendLine("                {");
        builder.AppendLine("                    GCHandle.FromIntPtr(h).Free();");
        builder.AppendLine("                }");
        builder.AppendLine("                _trackedHandles.Clear();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static readonly object SyncRoot = new();");
        builder.AppendLine("    private static readonly Dictionary<nint, RegistrationState> Registrations = new();");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.BunValue Register(global::BunSharp.BunContext context)");
        builder.AppendLine("    {");
        builder.AppendLine("        ArgumentNullException.ThrowIfNull(context);");
        builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: true);");
        builder.AppendLine("        return registration.Constructor;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static RegistrationState GetOrCreate(global::BunSharp.BunContext context, bool publishGlobal)");
        builder.AppendLine("    {");
        builder.AppendLine("        lock (SyncRoot)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (Registrations.TryGetValue(context.Handle, out var existing))");
        builder.AppendLine("            {");
        builder.AppendLine("                if (publishGlobal)");
        builder.AppendLine("                {");
        builder.Append("                    PublishGlobal(context, existing.Constructor, ");
        builder.Append(CSharpLiteral(model.ExportName));
        builder.AppendLine(");");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                return existing;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.Append("            var definition = new global::BunSharp.BunClassDefinition(");
        builder.Append(CSharpLiteral(model.ExportName));
        builder.AppendLine(");");
        foreach (var method in model.InstanceMethods)
        {
            builder.Append("            definition.Methods.Add(new global::BunSharp.BunClassMethodDefinition(");
            builder.Append(CSharpLiteral(method.ExportName));
            builder.Append(", ");
            builder.Append(method.WrapperName);
            builder.Append(", argCount: ");
            builder.Append(method.Parameters.Length.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("));");
        }
        foreach (var property in model.InstanceProperties)
        {
            builder.Append("            definition.Properties.Add(new global::BunSharp.BunClassPropertyDefinition(");
            builder.Append(CSharpLiteral(property.ExportName));
            builder.Append(", getter: ");
            builder.Append(property.HasGetter ? property.GetterName : "null");
            builder.Append(", setter: ");
            builder.Append(property.HasSetter ? property.SetterName : "null");
            builder.Append(", readOnly: ");
            builder.Append(property.HasSetter ? "false" : "true");
            builder.AppendLine("));");
        }
        builder.AppendLine("            definition.Constructor = ConstructorCallback;");
        builder.Append("            definition.ConstructorArgCount = ");
        builder.Append(model.ConstructorParameters.Length.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(";");
        foreach (var method in model.StaticMethods)
        {
            builder.Append("            definition.StaticMethods.Add(new global::BunSharp.BunClassStaticMethodDefinition(");
            builder.Append(CSharpLiteral(method.ExportName));
            builder.Append(", ");
            builder.Append(method.WrapperName);
            builder.Append(", argCount: ");
            builder.Append(method.Parameters.Length.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("));");
        }
        foreach (var property in model.StaticProperties)
        {
            builder.Append("            definition.StaticProperties.Add(new global::BunSharp.BunClassStaticPropertyDefinition(");
            builder.Append(CSharpLiteral(property.ExportName));
            builder.Append(", getter: ");
            builder.Append(property.HasGetter ? property.GetterName : "null");
            builder.Append(", setter: ");
            builder.Append(property.HasSetter ? property.SetterName : "null");
            builder.Append(", readOnly: ");
            builder.Append(property.HasSetter ? "false" : "true");
            builder.AppendLine("));");
        }
        builder.AppendLine("            var @class = context.RegisterClass(definition);");
        builder.AppendLine("            var constructor = @class.Constructor;");
        builder.AppendLine("            if (context.IsUndefined(constructor))");
        builder.AppendLine("            {");
        builder.Append("                throw new InvalidOperationException(");
        builder.Append(CSharpLiteral($"Class '{model.ExportName}' did not expose a JS constructor."));
        builder.AppendLine(");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var registration = new RegistrationState(@class, constructor);");
        builder.AppendLine("            Registrations.Add(context.Handle, registration);");
        builder.AppendLine("            var contextHandle = context.Handle;");
        builder.AppendLine("            context.RegisterCleanup(() =>");
        builder.AppendLine("            {");
        builder.AppendLine("                lock (SyncRoot)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (Registrations.Remove(contextHandle, out var removed))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        removed.ReleaseTrackedHandles();");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.AppendLine("            });");
        builder.AppendLine("            if (publishGlobal)");
        builder.AppendLine("            {");
        builder.Append("                PublishGlobal(context, constructor, ");
        builder.Append(CSharpLiteral(model.ExportName));
        builder.AppendLine(");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return registration;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static void PublishGlobal(global::BunSharp.BunContext context, global::BunSharp.BunValue constructor, string exportName)");
        builder.AppendLine("    {");
        builder.AppendLine("        __JSExportCommon.EnsurePropertySet(context.SetProperty(context.GlobalObject, exportName, constructor), exportName);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    private static global::BunSharp.BunValue ConstructorCallback(global::BunSharp.BunContext context, nint classHandle, global::System.ReadOnlySpan<global::BunSharp.BunValue> args, nint userdata)");
        builder.AppendLine();
        builder.AppendLine("    {");
        builder.AppendLine("        _ = classHandle;");
        builder.AppendLine("        _ = userdata;");
        builder.Append("        __JSExportCommon.EnsureArgumentCount(");
        builder.Append(CSharpLiteral(model.ExportName));
        builder.Append(", args, ");
        builder.Append(model.ConstructorParameters.Length.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(");");
        AppendParameterReads(builder, model.ConstructorParameters, "        ");
        builder.Append("        var instance = new ");
        builder.Append(model.FullyQualifiedTypeName);
        builder.Append('(');
        builder.Append(string.Join(", ", model.ConstructorParameters.Select(static parameter => parameter.LocalName)));
        builder.AppendLine(");");
        builder.AppendLine("        return WrapManaged(context, instance);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    private static global::BunSharp.BunValue WrapManaged(global::BunSharp.BunContext context, ");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("? instance)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (instance is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            return global::BunSharp.BunValue.Null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
        builder.AppendLine("        var handle = GCHandle.Alloc(instance, GCHandleType.Normal);");
        builder.AppendLine("        var handlePtr = GCHandle.ToIntPtr(handle);");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            var value = registration.Class.CreateInstance(handlePtr, ReleaseHandle, context.Handle);");
        builder.AppendLine("            registration.TrackHandle(handlePtr);");
        builder.AppendLine("            return value;");
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            handle.Free();");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    private static ");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("? UnwrapManaged(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
        builder.AppendLine("        var nativePtr = registration.Class.Unwrap(value);");
        builder.AppendLine("        if (nativePtr == 0)");
        builder.AppendLine("        {");
        builder.Append("            throw new InvalidOperationException(");
        builder.Append(CSharpLiteral($"Expected JS instance of '{model.ExportName}'."));
        builder.AppendLine(");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var handle = GCHandle.FromIntPtr(nativePtr);");
        builder.Append("        return (");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("?)handle.Target;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static void ReleaseHandle(nint nativePtr, nint userdata)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (nativePtr == 0) return;");
        builder.AppendLine("        lock (SyncRoot)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!Registrations.TryGetValue(userdata, out var reg) || !reg.UntrackHandle(nativePtr))");
        builder.AppendLine("            {");
        builder.AppendLine("                return;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        GCHandle.FromIntPtr(nativePtr).Free();");
        builder.AppendLine("    }");
        builder.AppendLine();

        foreach (var method in model.InstanceMethods)
        {
            AppendInstanceMethod(builder, model, method);
        }

        foreach (var method in model.StaticMethods)
        {
            AppendStaticMethod(builder, model, method);
        }

        foreach (var property in model.InstanceProperties)
        {
            AppendInstanceProperty(builder, model, property);
        }

        foreach (var property in model.StaticProperties)
        {
            AppendStaticProperty(builder, model, property);
        }

        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendInstanceMethod(StringBuilder builder, ExportedTypeModel model, ExportedMethodModel method)
    {
        builder.Append("    private static global::BunSharp.BunValue ");
        builder.Append(method.WrapperName);
        builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue thisValue, nint nativePtr, global::System.ReadOnlySpan<global::BunSharp.BunValue> args, nint userdata)");
        builder.AppendLine("    {");
        builder.AppendLine("        _ = thisValue;");
        builder.AppendLine("        _ = userdata;");
        builder.Append("        __JSExportCommon.EnsureArgumentCount(");
        builder.Append(CSharpLiteral($"{model.ExportName}.{method.ExportName}"));
        builder.Append(", args, ");
        builder.Append(method.Parameters.Length.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(");");
        builder.Append("        var target = (");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine(")GCHandle.FromIntPtr(nativePtr).Target!;");
        AppendParameterReads(builder, method.Parameters, "        ");
        AppendInvocation(builder, method.ReturnType, $"target.{method.MemberName}({string.Join(", ", method.Parameters.Select(static parameter => parameter.LocalName))})", "        ");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendInstanceProperty(StringBuilder builder, ExportedTypeModel model, ExportedPropertyModel property)
    {
        if (property.HasGetter)
        {
            builder.Append("    private static global::BunSharp.BunValue ");
            builder.Append(property.GetterName);
            builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue thisValue, nint nativePtr, nint userdata)");
            builder.AppendLine("    {");
            builder.Append("        var target = (");
            builder.Append(model.FullyQualifiedTypeName);
            builder.AppendLine(")GCHandle.FromIntPtr(nativePtr).Target!;");
            AppendInvocation(builder, property.PropertyType, $"target.{property.MemberName}", "        ");
            builder.AppendLine("    }");
            builder.AppendLine();
        }

        if (property.HasSetter)
        {
            builder.Append("    private static void ");
            builder.Append(property.SetterName);
            builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue thisValue, nint nativePtr, global::BunSharp.BunValue value, nint userdata)");
            builder.AppendLine("    {");
            builder.Append("        var target = (");
            builder.Append(model.FullyQualifiedTypeName);
            builder.AppendLine(")GCHandle.FromIntPtr(nativePtr).Target!;");
            builder.Append("        target.");
            builder.Append(property.MemberName);
            builder.Append(" = ");
            builder.Append(ConvertFromBunValue(property.PropertyType, "value", "context"));
            builder.AppendLine(";");
            builder.AppendLine("    }");
            builder.AppendLine();
        }
    }

    private static void AppendStaticProperty(StringBuilder builder, ExportedTypeModel model, ExportedPropertyModel property)
    {
        if (property.HasGetter)
        {
            builder.Append("    private static global::BunSharp.BunValue ");
            builder.Append(property.GetterName);
            builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue thisValue, nint userdata)");
            builder.AppendLine("    {");
            builder.AppendLine("        _ = thisValue;");
            builder.AppendLine("        _ = userdata;");
            AppendInvocation(builder, property.PropertyType, $"{model.FullyQualifiedTypeName}.{property.MemberName}", "        ");
            builder.AppendLine("    }");
            builder.AppendLine();
        }

        if (property.HasSetter)
        {
            builder.Append("    private static void ");
            builder.Append(property.SetterName);
            builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue thisValue, global::BunSharp.BunValue value, nint userdata)");
            builder.AppendLine("    {");
            builder.AppendLine("        _ = thisValue;");
            builder.AppendLine("        _ = userdata;");
            builder.Append(model.FullyQualifiedTypeName);
            builder.Append('.');
            builder.Append(property.MemberName);
            builder.Append(" = ");
            builder.Append(ConvertFromBunValue(property.PropertyType, "value", "context"));
            builder.AppendLine(";");
            builder.AppendLine("    }");
            builder.AppendLine();
        }
    }

    private static void AppendStaticMethod(StringBuilder builder, ExportedTypeModel model, ExportedMethodModel method)
    {
        builder.Append("    private static global::BunSharp.BunValue ");
        builder.Append(method.WrapperName);
        builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue thisValue, global::System.ReadOnlySpan<global::BunSharp.BunValue> args, nint userdata)");
        builder.AppendLine("    {");
        builder.AppendLine("        _ = thisValue;");
        builder.AppendLine("        _ = userdata;");
        builder.Append("        __JSExportCommon.EnsureArgumentCount(");
        builder.Append(CSharpLiteral($"{model.ExportName}.{method.ExportName}"));
        builder.Append(", args, ");
        builder.Append(method.Parameters.Length.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(");");
        AppendParameterReads(builder, method.Parameters, "        ");
        AppendInvocation(builder, method.ReturnType, $"{model.FullyQualifiedTypeName}.{method.MemberName}({string.Join(", ", method.Parameters.Select(static parameter => parameter.LocalName))})", "        ");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendParameterReads(StringBuilder builder, ImmutableArray<ParameterModel> parameters, string indent)
    {
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            builder.Append(indent);
            builder.Append("var ");
            builder.Append(parameter.LocalName);
            builder.Append(" = ");
            builder.Append(ConvertFromBunValue(parameter.Type, $"args[{index}]", "context"));
            builder.AppendLine(";");
        }
    }

    private static void AppendInvocation(StringBuilder builder, TypeRefModel returnType, string expression, string indent)
    {
        if (returnType.Kind == ExportValueKind.Void)
        {
            builder.Append(indent);
            builder.Append(expression);
            builder.AppendLine(";");
            builder.Append(indent);
            builder.AppendLine("return global::BunSharp.BunValue.Undefined;");
            return;
        }

        builder.Append(indent);
        builder.Append("var result = ");
        builder.Append(expression);
        builder.AppendLine(";");
        builder.Append(indent);
        builder.Append("return ");
        builder.Append(ConvertToBunValue(returnType, "result", "context"));
        builder.AppendLine(";");
    }

    private static string ConvertFromBunValue(TypeRefModel type, string valueExpression, string contextExpression)
    {
        var expression = type.Kind switch
        {
            ExportValueKind.Bool => $"{contextExpression}.ToBoolean({valueExpression})",
            ExportValueKind.Int32 => $"{contextExpression}.ToInt32({valueExpression})",
            ExportValueKind.Double => $"{contextExpression}.ToNumber({valueExpression})",
            ExportValueKind.String => $"__JSExportCommon.ReadString({contextExpression}, {valueExpression})",
            ExportValueKind.ByteArray => $"__JSExportCommon.ReadByteArray({contextExpression}, {valueExpression})",
            ExportValueKind.BunValue => valueExpression,
            ExportValueKind.ExportedObject => $"{type.HelperId}.UnwrapManaged({contextExpression}, {valueExpression})",
            _ => throw new InvalidOperationException($"Unsupported conversion kind {type.Kind}.")
        };

        if (type.Kind is ExportValueKind.String or ExportValueKind.ByteArray or ExportValueKind.ExportedObject)
        {
            return type.IsNullableReferenceType ? expression : expression + "!";
        }

        return expression;
    }

    private static string ConvertToBunValue(TypeRefModel type, string valueExpression, string contextExpression)
    {
        return type.Kind switch
        {
            ExportValueKind.Bool => $"{contextExpression}.CreateBoolean({valueExpression})",
            ExportValueKind.Int32 => $"{contextExpression}.CreateInt32({valueExpression})",
            ExportValueKind.Double => $"{contextExpression}.CreateNumber({valueExpression})",
            ExportValueKind.String => $"{valueExpression} is null ? global::BunSharp.BunValue.Null : {contextExpression}.CreateString({valueExpression})",
            ExportValueKind.ByteArray => $"__JSExportCommon.CreateByteArray({contextExpression}, {valueExpression})",
            ExportValueKind.BunValue => valueExpression,
            ExportValueKind.ExportedObject => $"{type.HelperId}.WrapManaged({contextExpression}, {valueExpression})",
            _ => throw new InvalidOperationException($"Unsupported conversion kind {type.Kind}.")
        };
    }

    private static string CreateTypeId(INamedTypeSymbol type)
    {
        return "__JSExport_" + type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            .Replace('.', '_')
            .Replace('+', '_')
            .Replace('<', '_')
            .Replace('>', '_');
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
        {
            return name;
        }

        var chars = name.ToCharArray();
        chars[0] = char.ToLowerInvariant(chars[0]);
        return new string(chars);
    }

    private static string CSharpLiteral(string value)
    {
        return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);
    }

    private readonly struct ExportRule
    {
        public ExportRule(bool enabled, string? name)
        {
            Enabled = enabled;
            Name = name;
        }

        public bool Enabled { get; }

        public string? Name { get; }
    }

    private readonly struct ExportedTypeModel
    {
        public ExportedTypeModel(
            string id,
            string typeName,
            string exportName,
            string fullyQualifiedTypeName,
            ImmutableArray<ParameterModel> constructorParameters,
            List<ExportedMethodModel> instanceMethods,
            List<ExportedMethodModel> staticMethods,
            List<ExportedPropertyModel> instanceProperties,
            List<ExportedPropertyModel> staticProperties)
        {
            Id = id;
            TypeName = typeName;
            ExportName = exportName;
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            ConstructorParameters = constructorParameters;
            InstanceMethods = instanceMethods;
            StaticMethods = staticMethods;
            InstanceProperties = instanceProperties;
            StaticProperties = staticProperties;
        }

        public string Id { get; }

        public string TypeName { get; }

        public string ExportName { get; }

        public string FullyQualifiedTypeName { get; }

        public ImmutableArray<ParameterModel> ConstructorParameters { get; }

        public List<ExportedMethodModel> InstanceMethods { get; }

        public List<ExportedMethodModel> StaticMethods { get; }

        public List<ExportedPropertyModel> InstanceProperties { get; }

        public List<ExportedPropertyModel> StaticProperties { get; }
    }

    private readonly struct ExportedMethodModel
    {
        public ExportedMethodModel(
            string memberName,
            string exportName,
            bool isStatic,
            ImmutableArray<ParameterModel> parameters,
            TypeRefModel returnType)
        {
            MemberName = memberName;
            ExportName = exportName;
            IsStatic = isStatic;
            Parameters = parameters;
            ReturnType = returnType;
        }

        public string MemberName { get; }

        public string ExportName { get; }

        public bool IsStatic { get; }

        public ImmutableArray<ParameterModel> Parameters { get; }

        public TypeRefModel ReturnType { get; }

        public string WrapperName => IsStatic ? $"StaticMethod_{MemberName}" : $"InstanceMethod_{MemberName}";
    }

    private readonly struct ExportedPropertyModel
    {
        public ExportedPropertyModel(
            string memberName,
            string exportName,
            bool isStatic,
            bool hasGetter,
            bool hasSetter,
            TypeRefModel propertyType)
        {
            MemberName = memberName;
            ExportName = exportName;
            IsStatic = isStatic;
            HasGetter = hasGetter;
            HasSetter = hasSetter;
            PropertyType = propertyType;
        }

        public string MemberName { get; }

        public string ExportName { get; }

        public bool IsStatic { get; }

        public bool HasGetter { get; }

        public bool HasSetter { get; }

        public TypeRefModel PropertyType { get; }

        public string GetterName => IsStatic ? $"StaticGetter_{MemberName}" : $"InstanceGetter_{MemberName}";

        public string SetterName => IsStatic ? $"StaticSetter_{MemberName}" : $"InstanceSetter_{MemberName}";
    }

    private readonly struct ParameterModel
    {
        public ParameterModel(string name, TypeRefModel type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }

        public TypeRefModel Type { get; }

        public string LocalName => Name + "Arg";
    }

    private readonly struct TypeRefModel
    {
        public TypeRefModel(ExportValueKind kind, string fullyQualifiedTypeName, string? helperId, bool isNullableReferenceType)
        {
            Kind = kind;
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            HelperId = helperId;
            IsNullableReferenceType = isNullableReferenceType;
        }

        public ExportValueKind Kind { get; }

        public string FullyQualifiedTypeName { get; }

        public string? HelperId { get; }

        public bool IsNullableReferenceType { get; }
    }

    private enum ExportValueKind
    {
        Bool,
        Int32,
        Double,
        String,
        ByteArray,
        BunValue,
        ExportedObject,
        Void,
    }
}