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
    private const string JsObjectRefMetadataName = "BunSharp.JSObjectRef";
    private const string JsFunctionRefMetadataName = "BunSharp.JSFunctionRef";
    private const string JsArrayRefMetadataName = "BunSharp.JSArrayRef";
    private const string JsArrayBufferRefMetadataName = "BunSharp.JSArrayBufferRef";
    private const string JsTypedArrayRefMetadataName = "BunSharp.JSTypedArrayRef";
    private const string JsBufferRefMetadataName = "BunSharp.JSBufferRef";
    private const string DiagnosticCategory = "BunSharp.JSExport";

    private static readonly DiagnosticDescriptor UnsupportedTypeDescriptor = new(
        id: "BSG001",
        title: "Unsupported export type",
        messageFormat: "Member '{0}' uses unsupported type '{1}'. Supported types are bool, int, double, string, byte[], T[], BunValue, JSObjectRef, JSFunctionRef, JSArrayRef, JSArrayBufferRef, JSTypedArrayRef, JSBufferRef, void, and JS-exported classes in the same assembly.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MultipleConstructorsDescriptor = new(
        id: "BSG002",
        title: "Unsupported constructor shape",
        messageFormat: "Type '{0}' must declare exactly one public instance constructor to be JS-exportable.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateExportNameDescriptor = new(
        id: "BSG003",
        title: "Duplicate export name",
        messageFormat: "Type '{0}' has multiple exported {1} members named '{2}'.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidMemberContainerDescriptor = new(
        id: "BSG004",
        title: "Member export requires exported class",
        messageFormat: "Member '{0}' cannot use JSExportAttribute because containing type '{1}' is not exported.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMemberShapeDescriptor = new(
        id: "BSG005",
        title: "Unsupported export member",
        messageFormat: "Member '{0}' is not supported for JS export: {1}.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedStableDescriptor = new(
        id: "BSG006",
        title: "Unsupported stable JS identity option",
        messageFormat: "Member '{0}' cannot use Stable with type '{1}'. Stable is currently supported only on exported byte[] and T[] properties and method return values.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidDelegateStableDescriptor = new(
        id: "BSG007",
        title: "Delegate exports are always stable",
        messageFormat: "Member '{0}' uses a delegate export shape and cannot declare Stable = false. Delegate exports always use stable JS function reference semantics.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMemberAccessibilityDescriptor = new(
        id: "BSG008",
        title: "Unsupported export member accessibility",
        messageFormat: "Member '{0}' cannot use JSExportAttribute with accessibility '{1}'. JSExport supports public members, and explicitly annotated internal members only.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedTypeAccessibilityDescriptor = new(
        id: "BSG009",
        title: "Unsupported export type accessibility",
        messageFormat: "Type '{0}' cannot use JSExportAttribute with accessibility '{1}'. JSExport supports public and internal classes only.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedStaticJsReferenceExportDescriptor = new(
        id: "BSG010",
        title: "Unsupported static JS reference export",
        messageFormat: "Member '{0}' cannot be exported as a static JS reference member because type '{1}' is runtime-affine. Static JS reference properties and method return values are not supported.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedStaticDelegateExportDescriptor = new(
        id: "BSG011",
        title: "Unsupported static delegate export",
        messageFormat: "Member '{0}' cannot be exported as a static delegate member. Static delegate properties and method return values are not supported because delegate exports may capture runtime-affine JS function state.",
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
        var jsObjectRefSymbol = compilation.GetTypeByMetadataName(JsObjectRefMetadataName);
        var jsFunctionRefSymbol = compilation.GetTypeByMetadataName(JsFunctionRefMetadataName);
        var jsArrayRefSymbol = compilation.GetTypeByMetadataName(JsArrayRefMetadataName);
        var jsArrayBufferRefSymbol = compilation.GetTypeByMetadataName(JsArrayBufferRefMetadataName);
        var jsTypedArrayRefSymbol = compilation.GetTypeByMetadataName(JsTypedArrayRefMetadataName);
        var jsBufferRefSymbol = compilation.GetTypeByMetadataName(JsBufferRefMetadataName);

        if (jsExportAttributeSymbol is null || bunValueSymbol is null || bunContextSymbol is null || jsObjectRefSymbol is null || jsFunctionRefSymbol is null || jsArrayRefSymbol is null || jsArrayBufferRefSymbol is null || jsTypedArrayRefSymbol is null || jsBufferRefSymbol is null)
        {
            return;
        }

        var knownTypes = new KnownTypeSymbols(bunValueSymbol, jsObjectRefSymbol, jsFunctionRefSymbol, jsArrayRefSymbol, jsArrayBufferRefSymbol, jsTypedArrayRefSymbol, jsBufferRefSymbol);

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
            if (!TryBuildTypeModel(context, type, jsExportAttributeSymbol, knownTypes, exportedTypeNames, out var model))
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
        KnownTypeSymbols knownTypes,
        ISet<string> exportedTypeNames,
        out ExportedTypeModel model)
    {
        model = default!;

        if (!TryResolveExportRule(type, jsExportAttributeSymbol, out var typeRule) || !typeRule.Enabled)
        {
            return false;
        }

        if (!IsExportableTypeAccessibility(type.DeclaredAccessibility))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedTypeAccessibilityDescriptor,
                type.Locations.FirstOrDefault(),
                type.ToDisplayString(),
                type.DeclaredAccessibility.ToString().ToLowerInvariant()));
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
        if (!TryCreateParameters(context, constructor.Parameters, constructor.Name, knownTypes, exportedTypeNames, out var constructorParameters))
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
                    var hasExplicitMethodExport = TryGetExplicitEnabledExport(method, jsExportAttributeSymbol, out _);
                    if (!IsExportAccessible(method.DeclaredAccessibility, hasExplicitMethodExport))
                    {
                        if (hasExplicitMethodExport)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                UnsupportedMemberAccessibilityDescriptor,
                                method.Locations.FirstOrDefault(),
                                method.Name,
                                method.DeclaredAccessibility.ToString().ToLowerInvariant()));
                        }

                        continue;
                    }

                    if (!TryResolveMemberModel(context, type, method, jsExportAttributeSymbol, knownTypes, exportedTypeNames, out var methodModel))
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

                    var hasExplicitPropertyExport = TryGetExplicitEnabledExport(property, jsExportAttributeSymbol, out _);
                    var hasExportableGetter = IsExportAccessible(property.GetMethod?.DeclaredAccessibility, hasExplicitPropertyExport);
                    var hasExportableSetter = IsExportAccessible(property.SetMethod?.DeclaredAccessibility, hasExplicitPropertyExport);
                    if (!hasExportableGetter && !hasExportableSetter)
                    {
                        if (hasExplicitPropertyExport)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                UnsupportedMemberAccessibilityDescriptor,
                                property.Locations.FirstOrDefault(),
                                property.Name,
                                property.DeclaredAccessibility.ToString().ToLowerInvariant()));
                        }

                        continue;
                    }

                    if (!TryResolvePropertyModel(context, type, property, jsExportAttributeSymbol, knownTypes, exportedTypeNames, hasExportableGetter, hasExportableSetter, out var propertyModel))
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
        KnownTypeSymbols knownTypes,
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

        if (!TryCreateParameters(context, method.Parameters, method.Name, knownTypes, exportedTypeNames, out var parameters))
        {
            return false;
        }

        if (!TryCreateTypeRef(context, method.ReturnType, method.Name, knownTypes, exportedTypeNames, allowVoid: true, out var returnType))
        {
            return false;
        }

        if (method.IsStatic && IsRuntimeAffineStaticJsReference(returnType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedStaticJsReferenceExportDescriptor,
                method.Locations.FirstOrDefault(),
                method.Name,
                method.ReturnType.ToDisplayString()));
            return false;
        }

        if (method.IsStatic && returnType.IsDelegate)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedStaticDelegateExportDescriptor,
                method.Locations.FirstOrDefault(),
                method.Name));
            return false;
        }

        var effectiveStable = exportRule.Stable;
        if (returnType.IsDelegate)
        {
            if (exportRule.HasStableOption && !exportRule.Stable)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidDelegateStableDescriptor,
                    method.Locations.FirstOrDefault(),
                    method.Name));
                return false;
            }

            effectiveStable = true;
        }

        if (!returnType.IsDelegate && exportRule.Stable && !returnType.CanUseStableOption)
        {
            context.ReportDiagnostic(Diagnostic.Create(
            UnsupportedStableDescriptor,
                method.Locations.FirstOrDefault(),
                method.Name,
                method.ReturnType.ToDisplayString()));
            return false;
        }

        model = new ExportedMethodModel(
            method.Name,
            exportRule.Name ?? ToCamelCase(method.Name),
            method.IsStatic,
            parameters,
            returnType,
            effectiveStable);

        return true;
    }

    private static bool TryResolvePropertyModel(
        GeneratorExecutionContext context,
        INamedTypeSymbol containingType,
        IPropertySymbol property,
        INamedTypeSymbol jsExportAttributeSymbol,
        KnownTypeSymbols knownTypes,
        ISet<string> exportedTypeNames,
        bool hasGetter,
        bool hasSetter,
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
        if (!hasGetter && !hasSetter)
        {
            return true;
        }

        if (!TryCreateTypeRef(context, property.Type, property.Name, knownTypes, exportedTypeNames, allowVoid: false, out var propertyType))
        {
            return false;
        }

        if (property.IsStatic && IsRuntimeAffineStaticJsReference(propertyType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedStaticJsReferenceExportDescriptor,
                property.Locations.FirstOrDefault(),
                property.Name,
                property.Type.ToDisplayString()));
            return false;
        }

        if (property.IsStatic && propertyType.IsDelegate)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedStaticDelegateExportDescriptor,
                property.Locations.FirstOrDefault(),
                property.Name));
            return false;
        }

        var effectiveStable = exportRule.Stable;
        if (propertyType.IsDelegate)
        {
            if (exportRule.HasStableOption && !exportRule.Stable)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidDelegateStableDescriptor,
                    property.Locations.FirstOrDefault(),
                    property.Name));
                return false;
            }

            effectiveStable = true;
        }
        else if (exportRule.Stable && !propertyType.CanUseStableOption)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedStableDescriptor,
                property.Locations.FirstOrDefault(),
                property.Name,
                property.Type.ToDisplayString()));
            return false;
        }

        model = new ExportedPropertyModel(
            property.Name,
            exportRule.Name ?? ToCamelCase(property.Name),
            property.IsStatic,
            hasGetter,
            hasSetter,
            propertyType,
            effectiveStable);

        return true;
    }

    private static bool TryCreateParameters(
        GeneratorExecutionContext context,
        ImmutableArray<IParameterSymbol> parameters,
        string memberName,
        KnownTypeSymbols knownTypes,
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

            if (!TryCreateTypeRef(context, parameter.Type, memberName, knownTypes, exportedTypeNames, allowVoid: false, out var typeRef))
            {
                result = default;
                return false;
            }

            if (typeRef.IsDelegate)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedMemberShapeDescriptor,
                    parameter.Locations.FirstOrDefault(),
                    memberName,
                    "delegate parameters are currently supported only on exported properties"));
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
        KnownTypeSymbols knownTypes,
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

        if (type is IArrayTypeSymbol generalArrayType && generalArrayType.Rank == 1)
        {
            if (!TryCreateTypeRef(context, generalArrayType.ElementType, memberName, knownTypes, exportedTypeNames, allowVoid: false, out var elementTypeRef))
            {
                typeRef = default!;
                return false;
            }

            typeRef = new TypeRefModel(ExportValueKind.Array, fullyQualifiedName, null, isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated, elementType: elementTypeRef);
            return true;
        }

        if (SymbolEqualityComparer.Default.Equals(type, knownTypes.BunValueSymbol))
        {
            typeRef = new TypeRefModel(ExportValueKind.BunValue, fullyQualifiedName, null, isNullableReferenceType: false);
            return true;
        }

        if (type is INamedTypeSymbol namedType)
        {
            if (namedType.TypeKind == TypeKind.Delegate)
            {
                var invokeMethod = namedType.DelegateInvokeMethod;
                if (invokeMethod is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedTypeDescriptor,
                        type.Locations.FirstOrDefault(),
                        memberName,
                        type.ToDisplayString()));
                    typeRef = default!;
                    return false;
                }

                if (invokeMethod.ReturnsByRef || invokeMethod.ReturnsByRefReadonly)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedMemberShapeDescriptor,
                        type.Locations.FirstOrDefault(),
                        memberName,
                        "delegate ref returns are not supported"));
                    typeRef = default!;
                    return false;
                }

                var delegateParametersBuilder = ImmutableArray.CreateBuilder<ParameterModel>(invokeMethod.Parameters.Length);
                foreach (var parameter in invokeMethod.Parameters)
                {
                    if (parameter.RefKind != RefKind.None)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            UnsupportedMemberShapeDescriptor,
                            parameter.Locations.FirstOrDefault(),
                            memberName,
                            "delegate ref, in, and out parameters are not supported"));
                        typeRef = default!;
                        return false;
                    }

                    if (!TryCreateTypeRef(context, parameter.Type, memberName, knownTypes, exportedTypeNames, allowVoid: false, out var delegateParameterType))
                    {
                        typeRef = default!;
                        return false;
                    }

                    if (delegateParameterType.IsDelegate)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            UnsupportedMemberShapeDescriptor,
                            parameter.Locations.FirstOrDefault(),
                            memberName,
                            "nested delegate parameters are not supported"));
                        typeRef = default!;
                        return false;
                    }

                    delegateParametersBuilder.Add(new ParameterModel(parameter.Name, delegateParameterType));
                }

                if (!TryCreateTypeRef(context, invokeMethod.ReturnType, memberName, knownTypes, exportedTypeNames, allowVoid: true, out var delegateReturnType))
                {
                    typeRef = default!;
                    return false;
                }

                if (delegateReturnType.IsDelegate)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedMemberShapeDescriptor,
                        type.Locations.FirstOrDefault(),
                        memberName,
                        "delegate return values cannot themselves be delegates"));
                    typeRef = default!;
                    return false;
                }

                typeRef = new TypeRefModel(
                    ExportValueKind.Delegate,
                    fullyQualifiedName,
                    null,
                    isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated,
                    delegateSignature: new DelegateSignatureModel(delegateParametersBuilder.ToImmutable(), delegateReturnType));
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(namedType, knownTypes.JSObjectRefSymbol))
            {
                typeRef = new TypeRefModel(ExportValueKind.JSObjectRef, fullyQualifiedName, null, isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated);
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(namedType, knownTypes.JSFunctionRefSymbol))
            {
                typeRef = new TypeRefModel(ExportValueKind.JSFunctionRef, fullyQualifiedName, null, isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated);
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(namedType, knownTypes.JSArrayRefSymbol))
            {
                typeRef = new TypeRefModel(ExportValueKind.JSArrayRef, fullyQualifiedName, null, isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated);
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(namedType, knownTypes.JSArrayBufferRefSymbol))
            {
                typeRef = new TypeRefModel(ExportValueKind.JSArrayBufferRef, fullyQualifiedName, null, isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated);
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(namedType, knownTypes.JSTypedArrayRefSymbol))
            {
                typeRef = new TypeRefModel(ExportValueKind.JSTypedArrayRef, fullyQualifiedName, null, isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated);
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(namedType, knownTypes.JSBufferRefSymbol))
            {
                typeRef = new TypeRefModel(ExportValueKind.JSBufferRef, fullyQualifiedName, null, isNullableReferenceType: type.NullableAnnotation == NullableAnnotation.Annotated);
                return true;
            }

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

        typeRef = default!;
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

            var stable = false;
            var hasStableOption = false;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "Stable" &&
                    namedArgument.Value.Kind == TypedConstantKind.Primitive &&
                    namedArgument.Value.Type?.SpecialType == SpecialType.System_Boolean)
                {
                    hasStableOption = true;
                    stable |= namedArgument.Value.Value is true;
                }
            }

            if (attribute.ConstructorArguments.Length == 0)
            {
                rule = new ExportRule(true, null, stable, hasStableOption);
                return true;
            }

            var argument = attribute.ConstructorArguments[0];
            if (argument.Kind == TypedConstantKind.Primitive && argument.Type?.SpecialType == SpecialType.System_Boolean)
            {
                rule = new ExportRule(argument.Value is true, null, stable, hasStableOption);
                return true;
            }

            if (argument.Kind == TypedConstantKind.Primitive && argument.Type?.SpecialType == SpecialType.System_String)
            {
                rule = new ExportRule(true, (string?)argument.Value, stable, hasStableOption);
                return true;
            }
        }

        rule = new ExportRule(true, null, stable: false, hasStableOption: false);
        return !HasAttribute(symbol, jsExportAttributeSymbol) || rule.Enabled;
    }

    private static bool TryGetExplicitEnabledExport(ISymbol symbol, INamedTypeSymbol jsExportAttributeSymbol, out ExportRule rule)
    {
        if (!HasAttribute(symbol, jsExportAttributeSymbol))
        {
            rule = default;
            return false;
        }

        return TryResolveExportRule(symbol, jsExportAttributeSymbol, out rule) && rule.Enabled;
    }

    private static bool IsExportAccessible(Accessibility? accessibility, bool hasExplicitExport)
    {
        return accessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Internal when hasExplicitExport => true,
            _ => false,
        };
    }

    private static bool IsExportableTypeAccessibility(Accessibility accessibility)
    {
        return accessibility is Accessibility.Public or Accessibility.Internal;
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
            builder.Append(".Register(context); return true; }, (context, value) => ");
            builder.Append(model.Id);
            builder.Append(".TryUnwrapManaged(context, value, out var result) ? result : null, (context, instance) => instance is ");
            builder.Append(model.FullyQualifiedTypeName);
            builder.Append(" typed && ");
            builder.Append(model.Id);
            builder.AppendLine(".TryGetManagedWrapper(context, typed, out var value) ? value : (global::BunSharp.BunValue?)null);");
        }
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();

        var arrayTypes = CollectAllArrayTypeRefs(models);
        AppendCommonHelpers(builder, arrayTypes);

        foreach (var model in models)
        {
            AppendTypeModel(builder, model);
        }

        return builder.ToString();
    }

    private static List<TypeRefModel> CollectAllArrayTypeRefs(IReadOnlyList<ExportedTypeModel> models)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TypeRefModel>();

        void Visit(TypeRefModel typeRef)
        {
            if (typeRef.Kind == ExportValueKind.Array)
            {
                var helperName = GetArrayElementTypeSuffix(typeRef.ElementType!);
                if (seen.Add(helperName))
                {
                    // Visit the element type first so nested helpers are emitted before the outer helper.
                    Visit(typeRef.ElementType!);
                    result.Add(typeRef);
                }
            }
        }

        foreach (var model in models)
        {
            foreach (var p in model.ConstructorParameters) Visit(p.Type);
            foreach (var m in model.InstanceMethods) { Visit(m.ReturnType); foreach (var p in m.Parameters) Visit(p.Type); }
            foreach (var m in model.StaticMethods) { Visit(m.ReturnType); foreach (var p in m.Parameters) Visit(p.Type); }
            foreach (var prop in model.InstanceProperties) Visit(prop.PropertyType);
            foreach (var prop in model.StaticProperties) Visit(prop.PropertyType);
        }

        return result;
    }

    private static string GetArrayElementTypeSuffix(TypeRefModel elementType)
    {
        return elementType.Kind switch
        {
            ExportValueKind.Bool => "Bool",
            ExportValueKind.Int32 => "Int32",
            ExportValueKind.Double => "Double",
            ExportValueKind.String => "String",
            ExportValueKind.ByteArray => "ByteArray",
            ExportValueKind.BunValue => "BunValue",
            ExportValueKind.JSObjectRef => "JSObjectRef",
            ExportValueKind.JSFunctionRef => "JSFunctionRef",
            ExportValueKind.JSArrayRef => "JSArrayRef",
            ExportValueKind.JSArrayBufferRef => "JSArrayBufferRef",
            ExportValueKind.JSTypedArrayRef => "JSTypedArrayRef",
            ExportValueKind.JSBufferRef => "JSBufferRef",
            ExportValueKind.ExportedObject => elementType.HelperId!,
            ExportValueKind.Array => "Array_" + GetArrayElementTypeSuffix(elementType.ElementType!),
            _ => throw new InvalidOperationException($"Unsupported array element kind {elementType.Kind}."),
        };
    }

    private static string GetArrayReadHelperName(TypeRefModel arrayType)
    {
        return "ReadArray_" + GetArrayElementTypeSuffix(arrayType.ElementType!);
    }

    private static string GetArrayWriteHelperName(TypeRefModel arrayType)
    {
        return "CreateArray_" + GetArrayElementTypeSuffix(arrayType.ElementType!);
    }

    private static string GetArrayDisposeHelperName(TypeRefModel arrayType)
    {
        return "DisposeConstructedArray_" + GetArrayElementTypeSuffix(arrayType.ElementType!);
    }

    private static bool TypeNeedsArrayReadCleanup(TypeRefModel type)
    {
        return type.Kind switch
        {
            ExportValueKind.JSObjectRef => true,
            ExportValueKind.JSFunctionRef => true,
            ExportValueKind.JSArrayRef => true,
            ExportValueKind.JSArrayBufferRef => true,
            ExportValueKind.JSTypedArrayRef => true,
            ExportValueKind.JSBufferRef => true,
            ExportValueKind.Array => TypeNeedsArrayReadCleanup(type.ElementType!),
            _ => false,
        };
    }

    private static void AppendCommonHelpers(StringBuilder builder, List<TypeRefModel> arrayTypes)
    {
        builder.AppendLine("internal static class __JSExportCommon");
        builder.AppendLine("{");
        builder.AppendLine("    private sealed class NativeBufferOwner : global::System.IDisposable");
        builder.AppendLine("    {");
        builder.AppendLine("        private nint _buffer;");
        builder.AppendLine("        private nint _selfHandle;");
        builder.AppendLine("        private global::System.IDisposable? _cleanupRegistration;");
        builder.AppendLine();
        builder.AppendLine("        public NativeBufferOwner(nint buffer)");
        builder.AppendLine("        {");
        builder.AppendLine("            _buffer = buffer;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public nint UserData => _selfHandle;");
        builder.AppendLine();
        builder.AppendLine("        public void AttachCleanupRegistration(global::System.IDisposable cleanupRegistration)");
        builder.AppendLine("        {");
        builder.AppendLine("            _cleanupRegistration = cleanupRegistration;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void AttachHandle(global::System.Runtime.InteropServices.GCHandle handle)");
        builder.AppendLine("        {");
        builder.AppendLine("            _selfHandle = global::System.Runtime.InteropServices.GCHandle.ToIntPtr(handle);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void Dispose()");
        builder.AppendLine("        {");
        builder.AppendLine("            var buffer = global::System.Threading.Interlocked.Exchange(ref _buffer, 0);");
        builder.AppendLine("            if (buffer != 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                Marshal.FreeHGlobal(buffer);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            global::System.Threading.Interlocked.Exchange(ref _cleanupRegistration, null)?.Dispose();");
        builder.AppendLine();
        builder.AppendLine("            var selfHandle = global::System.Threading.Interlocked.Exchange(ref _selfHandle, 0);");
        builder.AppendLine("            if (selfHandle != 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::System.Runtime.InteropServices.GCHandle.FromIntPtr(selfHandle).Free();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static void EnsurePropertySet(bool result, string name)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!result)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new InvalidOperationException($\"Failed to set JS property '{name}'.\");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static void EnsureIndexSet(bool result, int index)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!result)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new InvalidOperationException($\"Failed to set JS array element at index {index}.\");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static void ReleaseOwnedUnmanagedBuffer(nint userdata)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (userdata == 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var handle = global::System.Runtime.InteropServices.GCHandle.FromIntPtr(userdata);");
        builder.AppendLine("        if (handle.Target is NativeBufferOwner owner)");
        builder.AppendLine("        {");
        builder.AppendLine("            owner.Dispose();");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private const int ArrayTransferBatchSize = 256;");
        builder.AppendLine();
        builder.AppendLine("    public static void EnsureArrayRangeRead(global::BunSharp.BunContext context, bool result, int start, int count)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!result)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new InvalidOperationException(context.GetLastError() ?? $\"Failed to read JS array range starting at index {start} with count {count}.\");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static void EnsureArrayRangeSet(global::BunSharp.BunContext context, bool result, int start, int count)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!result)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new InvalidOperationException(context.GetLastError() ?? $\"Failed to write JS array range starting at index {start} with count {count}.\");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static void DisposeReplacedReference<T>(T? current, T? next) where T : class, IDisposable");
        builder.AppendLine("    {");
        builder.AppendLine("        if (current is not null && !ReferenceEquals(current, next))");
        builder.AppendLine("        {");
        builder.AppendLine("            current.Dispose();");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static void DisposeFailedReferenceAssignment<T>(T? current, T? attempted) where T : class, IDisposable");
        builder.AppendLine("    {");
        builder.AppendLine("        if (attempted is not null && !ReferenceEquals(current, attempted))");
        builder.AppendLine("        {");
        builder.AppendLine("            attempted.Dispose();");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static void DisposeReference(global::System.IDisposable? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        value?.Dispose();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public readonly struct DelegateBinding<TDelegate> where TDelegate : class");
        builder.AppendLine("    {");
        builder.AppendLine("        public DelegateBinding(TDelegate? value, global::BunSharp.BunValue cachedValue, global::System.IDisposable? ownedReference, bool hasCachedValue)");
        builder.AppendLine("        {");
        builder.AppendLine("            Value = value;");
        builder.AppendLine("            CachedValue = cachedValue;");
        builder.AppendLine("            OwnedReference = ownedReference;");
        builder.AppendLine("            HasCachedValue = hasCachedValue;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public TDelegate? Value { get; }");
        builder.AppendLine();
        builder.AppendLine("        public global::BunSharp.BunValue CachedValue { get; }");
        builder.AppendLine();
        builder.AppendLine("        public global::System.IDisposable? OwnedReference { get; }");
        builder.AppendLine();
        builder.AppendLine("        public bool HasCachedValue { get; }");
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
        builder.AppendLine("        static byte[] AllocateAndCopy(nint source, int byteLength)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (byteLength == 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                return Array.Empty<byte>();");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var result = GC.AllocateUninitializedArray<byte>(byteLength);");
        builder.AppendLine("            Marshal.Copy(source, result, 0, byteLength);");
        builder.AppendLine("            return result;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (context.TryGetTypedArray(value, out var typedArray))");
        builder.AppendLine("        {");
        builder.AppendLine("            if (typedArray.Kind != global::BunSharp.Interop.BunTypedArrayKind.Uint8Array)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new InvalidOperationException(\"Expected Uint8Array for byte[] export.\");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var byteLength = checked((int)typedArray.ByteLength);");
        builder.AppendLine("            return AllocateAndCopy(IntPtr.Add(typedArray.Data, checked((int)typedArray.ByteOffset)), byteLength);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (context.TryGetArrayBuffer(value, out var arrayBuffer))");
        builder.AppendLine("        {");
        builder.AppendLine("            var byteLength = checked((int)arrayBuffer.ByteLength);");
        builder.AppendLine("            return AllocateAndCopy(arrayBuffer.Data, byteLength);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        throw new InvalidOperationException(\"Expected Uint8Array or ArrayBuffer for byte[] export.\");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.JSObjectRef? ReadJSObjectRef(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new global::BunSharp.JSObjectRef(context, value);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.JSFunctionRef? ReadJSFunctionRef(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new global::BunSharp.JSFunctionRef(context, value);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.JSArrayRef? ReadJSArrayRef(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new global::BunSharp.JSArrayRef(context, value);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.JSArrayBufferRef? ReadJSArrayBufferRef(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new global::BunSharp.JSArrayBufferRef(context, value);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.JSTypedArrayRef? ReadJSTypedArrayRef(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new global::BunSharp.JSTypedArrayRef(context, value);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.JSBufferRef? ReadJSBufferRef(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new global::BunSharp.JSBufferRef(context, value);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.BunValue ReferenceValueOrNull(global::BunSharp.JSObjectRef? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        return value is null ? global::BunSharp.BunValue.Null : value.Value;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.BunValue ReferenceValueOrNull(global::BunSharp.JSFunctionRef? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        return value is null ? global::BunSharp.BunValue.Null : value.Value;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.BunValue ReferenceValueOrNull(global::BunSharp.JSArrayRef? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        return value is null ? global::BunSharp.BunValue.Null : value.Value;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.BunValue ReferenceValueOrNull(global::BunSharp.JSArrayBufferRef? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        return value is null ? global::BunSharp.BunValue.Null : value.Value;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.BunValue ReferenceValueOrNull(global::BunSharp.JSTypedArrayRef? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        return value is null ? global::BunSharp.BunValue.Null : value.Value;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.BunValue ReferenceValueOrNull(global::BunSharp.JSBufferRef? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        return value is null ? global::BunSharp.BunValue.Null : value.Value;");
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
        builder.AppendLine("        if (context.CanRetainManagedResources)");
        builder.AppendLine("        {");
        builder.AppendLine("            var buffer = Marshal.AllocHGlobal(value.Length);");
        builder.AppendLine("            var owner = new NativeBufferOwner(buffer);");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                Marshal.Copy(value, 0, buffer, value.Length);");
        builder.AppendLine("                owner.AttachCleanupRegistration(context.RegisterCleanup(owner.Dispose));");
        builder.AppendLine("                owner.AttachHandle(global::System.Runtime.InteropServices.GCHandle.Alloc(owner));");
        builder.AppendLine("                return context.CreateTypedArray(global::BunSharp.Interop.BunTypedArrayKind.Uint8Array, buffer, checked((nuint)value.Length), ReleaseOwnedUnmanagedBuffer, owner.UserData);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                owner.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var arrayLength = checked((nuint)value.Length);");
        builder.AppendLine("        var array = context.CreateArray(arrayLength);");
        builder.AppendLine("        var pooledValues = global::System.Buffers.ArrayPool<global::BunSharp.BunValue>.Shared.Rent(global::System.Math.Min(value.Length, ArrayTransferBatchSize));");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            for (var offset = 0; offset < value.Length; offset += ArrayTransferBatchSize)");
        builder.AppendLine("            {");
        builder.AppendLine("                var count = global::System.Math.Min(value.Length - offset, ArrayTransferBatchSize);");
        builder.AppendLine("                var chunk = pooledValues.AsSpan(0, count);");
        builder.AppendLine("                for (var i = 0; i < count; i++)");
        builder.AppendLine("                {");
        builder.AppendLine("                    chunk[i] = context.CreateInt32(value[offset + i]);");
        builder.AppendLine("                }");
        builder.AppendLine("                EnsureArrayRangeSet(context, context.TrySetArrayRange(array, checked((uint)offset), chunk), offset, count);");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        builder.AppendLine("            global::System.Buffers.ArrayPool<global::BunSharp.BunValue>.Shared.Return(pooledValues);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var constructor = context.GetProperty(context.GlobalObject, \"Uint8Array\");");
        builder.AppendLine("        var from = context.GetProperty(constructor, \"from\");");
        builder.AppendLine("        Span<global::BunSharp.BunValue> args = stackalloc global::BunSharp.BunValue[1];");
        builder.AppendLine("        args[0] = array;");
        builder.AppendLine("        return context.Call(from, constructor, args);");
        builder.AppendLine("    }");

        foreach (var arrayType in arrayTypes)
        {
            AppendArrayReadHelper(builder, arrayType);
            AppendArrayWriteHelper(builder, arrayType);
            AppendArrayDisposeHelper(builder, arrayType);
        }

        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static string ElementReadExpression(TypeRefModel elementType, string valueExpr, string ctxExpr)
    {
        var expr = elementType.Kind switch
        {
            ExportValueKind.Bool => $"{ctxExpr}.ToBoolean({valueExpr})",
            ExportValueKind.Int32 => $"{ctxExpr}.ToInt32({valueExpr})",
            ExportValueKind.Double => $"{ctxExpr}.ToNumber({valueExpr})",
            ExportValueKind.String => $"ReadString({ctxExpr}, {valueExpr})",
            ExportValueKind.ByteArray => $"ReadByteArray({ctxExpr}, {valueExpr})",
            ExportValueKind.BunValue => valueExpr,
            ExportValueKind.JSObjectRef => $"ReadJSObjectRef({ctxExpr}, {valueExpr})",
            ExportValueKind.JSFunctionRef => $"ReadJSFunctionRef({ctxExpr}, {valueExpr})",
            ExportValueKind.JSArrayRef => $"ReadJSArrayRef({ctxExpr}, {valueExpr})",
            ExportValueKind.JSArrayBufferRef => $"ReadJSArrayBufferRef({ctxExpr}, {valueExpr})",
            ExportValueKind.JSTypedArrayRef => $"ReadJSTypedArrayRef({ctxExpr}, {valueExpr})",
            ExportValueKind.JSBufferRef => $"ReadJSBufferRef({ctxExpr}, {valueExpr})",
            ExportValueKind.ExportedObject => $"{elementType.HelperId}.UnwrapManaged({ctxExpr}, {valueExpr})",
            ExportValueKind.Array => $"{GetArrayReadHelperName(elementType)}({ctxExpr}, {valueExpr})",
            _ => throw new InvalidOperationException($"Unsupported element kind {elementType.Kind}.")
        };
        // Null-forgiving for reference-typed elements so the helper returns T[] not T?[]
        if (elementType.RequiresNullCheckedReferenceHandling)
            expr += "!";
        return expr;
    }

    private static string ElementWriteExpression(TypeRefModel elementType, string valueExpr, string ctxExpr)
    {
        return elementType.Kind switch
        {
            ExportValueKind.Bool => $"{ctxExpr}.CreateBoolean({valueExpr})",
            ExportValueKind.Int32 => $"{ctxExpr}.CreateInt32({valueExpr})",
            ExportValueKind.Double => $"{ctxExpr}.CreateNumber({valueExpr})",
            ExportValueKind.String => $"{valueExpr} is null ? global::BunSharp.BunValue.Null : {ctxExpr}.CreateString({valueExpr})",
            ExportValueKind.ByteArray => $"CreateByteArray({ctxExpr}, {valueExpr})",
            ExportValueKind.BunValue => valueExpr,
            ExportValueKind.JSObjectRef => $"{valueExpr} is null ? global::BunSharp.BunValue.Null : {valueExpr}.Value",
            ExportValueKind.JSFunctionRef => $"{valueExpr} is null ? global::BunSharp.BunValue.Null : {valueExpr}.Value",
            ExportValueKind.JSArrayRef => $"{valueExpr} is null ? global::BunSharp.BunValue.Null : {valueExpr}.Value",
            ExportValueKind.JSArrayBufferRef => $"{valueExpr} is null ? global::BunSharp.BunValue.Null : {valueExpr}.Value",
            ExportValueKind.JSTypedArrayRef => $"{valueExpr} is null ? global::BunSharp.BunValue.Null : {valueExpr}.Value",
            ExportValueKind.JSBufferRef => $"{valueExpr} is null ? global::BunSharp.BunValue.Null : {valueExpr}.Value",
            ExportValueKind.ExportedObject => $"{elementType.HelperId}.WrapManaged({ctxExpr}, {valueExpr})",
            ExportValueKind.Array => $"{GetArrayWriteHelperName(elementType)}({ctxExpr}, {valueExpr})",
            _ => throw new InvalidOperationException($"Unsupported element kind {elementType.Kind}.")
        };
    }

    private static void AppendArrayReadHelper(StringBuilder builder, TypeRefModel arrayType)
    {
        var elementType = arrayType.ElementType!;
        var helperName = GetArrayReadHelperName(arrayType);
        var csharpElementType = elementType.FullyQualifiedTypeName;
        var needsCleanup = TypeNeedsArrayReadCleanup(arrayType);
        var disposeHelperName = needsCleanup ? GetArrayDisposeHelperName(arrayType) : null;
        // For nested arrays (e.g. string[][]), the allocation needs the size in the
        // outermost bracket: new string[size][]. We use the array type's own FQN and
        // insert the size expression into the first "[]".
        var arrayFqn = arrayType.FullyQualifiedTypeName; // e.g. global::System.String[][]
        var allocExpr = arrayFqn.IndexOf("[]", StringComparison.Ordinal) is var idx && idx >= 0
            ? arrayFqn.Substring(0, idx) + "[checked((int)len)]" + arrayFqn.Substring(idx + 2)
            : csharpElementType + "[checked((int)len)]";

        builder.AppendLine();
        builder.Append("    public static ");
        builder.Append(csharpElementType);
        builder.Append("[]? ");
        builder.Append(helperName);
        builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value)) return null;");
        builder.AppendLine("        var len = context.GetArrayLength(value);");
        builder.AppendLine("        if (len < 0L) throw new InvalidOperationException(\"Expected a JS Array.\");");
        builder.Append("        var arr = new ");
        builder.Append(allocExpr);
        builder.AppendLine(";");
        builder.AppendLine("        if (arr.Length == 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            return arr;");
        builder.AppendLine("        }");
        builder.AppendLine();
        if (needsCleanup)
        {
            builder.AppendLine("        var constructedCount = 0;");
            builder.AppendLine();
        }
        builder.AppendLine("        var buffer = global::System.Buffers.ArrayPool<global::BunSharp.BunValue>.Shared.Rent(global::System.Math.Min(arr.Length, ArrayTransferBatchSize));");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            for (var offset = 0; offset < arr.Length; offset += ArrayTransferBatchSize)");
        builder.AppendLine("            {");
        builder.AppendLine("                var count = global::System.Math.Min(arr.Length - offset, ArrayTransferBatchSize);");
        builder.AppendLine("                var chunk = buffer.AsSpan(0, count);");
        builder.AppendLine("                EnsureArrayRangeRead(context, context.TryGetArrayRange(value, checked((uint)offset), chunk), offset, count);");
        builder.AppendLine("                for (var i = 0; i < count; i++)");
        builder.AppendLine("                {");
        builder.Append("                    arr[offset + i] = ");
        builder.Append(ElementReadExpression(elementType, "chunk[i]", "context"));
        builder.AppendLine(";");
        if (needsCleanup)
        {
            builder.AppendLine("                    constructedCount++;");
        }
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        if (needsCleanup)
        {
            builder.AppendLine("        catch");
            builder.AppendLine("        {");
            builder.Append("            ");
            builder.Append(disposeHelperName);
            builder.AppendLine("(arr, constructedCount);");
            builder.AppendLine("            throw;");
            builder.AppendLine("        }");
        }
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        builder.AppendLine("            global::System.Buffers.ArrayPool<global::BunSharp.BunValue>.Shared.Return(buffer);");
        builder.AppendLine("        }");
        builder.AppendLine("        return arr;");
        builder.AppendLine("    }");
    }

    private static void AppendArrayDisposeHelper(StringBuilder builder, TypeRefModel arrayType)
    {
        if (!TypeNeedsArrayReadCleanup(arrayType))
        {
            return;
        }

        var elementType = arrayType.ElementType!;
        var helperName = GetArrayDisposeHelperName(arrayType);

        builder.AppendLine();
        builder.Append("    private static void ");
        builder.Append(helperName);
        builder.Append("(");
        builder.Append(arrayType.FullyQualifiedTypeName);
        builder.AppendLine(" value, int count)");
        builder.AppendLine("    {");
        builder.AppendLine("        for (var i = 0; i < count; i++)");
        builder.AppendLine("        {");
        builder.AppendLine("            var item = value[i];");
        builder.AppendLine("            if (item is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                continue;");
        builder.AppendLine("            }");
        builder.AppendLine();
        if (elementType.Kind == ExportValueKind.Array)
        {
            builder.Append("            ");
            builder.Append(GetArrayDisposeHelperName(elementType));
            builder.AppendLine("(item, item.Length);");
        }
        else
        {
            builder.AppendLine("            item.Dispose();");
        }
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void AppendArrayWriteHelper(StringBuilder builder, TypeRefModel arrayType)
    {
        var elementType = arrayType.ElementType!;
        var helperName = GetArrayWriteHelperName(arrayType);
        var csharpElementType = elementType.FullyQualifiedTypeName;

        builder.AppendLine();
        builder.Append("    public static global::BunSharp.BunValue ");
        builder.Append(helperName);
        builder.Append("(global::BunSharp.BunContext context, ");
        builder.Append(csharpElementType);
        builder.AppendLine("[]? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (value is null) return global::BunSharp.BunValue.Null;");
        builder.AppendLine("        var jsArr = context.CreateArray((nuint)value.Length);");
        builder.AppendLine("        if (value.Length == 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            return jsArr;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var buffer = global::System.Buffers.ArrayPool<global::BunSharp.BunValue>.Shared.Rent(global::System.Math.Min(value.Length, ArrayTransferBatchSize));");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            for (var offset = 0; offset < value.Length; offset += ArrayTransferBatchSize)");
        builder.AppendLine("            {");
        builder.AppendLine("                var count = global::System.Math.Min(value.Length - offset, ArrayTransferBatchSize);");
        builder.AppendLine("                var chunk = buffer.AsSpan(0, count);");
        builder.AppendLine("                for (var i = 0; i < count; i++)");
        builder.AppendLine("                {");
        builder.Append("                    chunk[i] = ");
        builder.Append(ElementWriteExpression(elementType, "value[offset + i]", "context"));
        builder.AppendLine(";");
        builder.AppendLine("                }");
        builder.AppendLine("                EnsureArrayRangeSet(context, context.TrySetArrayRange(jsArr, checked((uint)offset), chunk), offset, count);");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        builder.AppendLine("            global::System.Buffers.ArrayPool<global::BunSharp.BunValue>.Shared.Return(buffer);");
        builder.AppendLine("        }");
        builder.AppendLine("        return jsArr;");
        builder.AppendLine("    }");
    }

    private static void AppendTypeModel(StringBuilder builder, ExportedTypeModel model)
    {
        builder.Append("internal static class ");
        builder.Append(model.Id);
        builder.AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    private sealed class ManagedWrapperEntry");
        builder.AppendLine("    {");
        builder.AppendLine("        public ManagedWrapperEntry(object instance, nint handle, global::BunSharp.BunValue value)");
        builder.AppendLine("        {");
        builder.AppendLine("            Instance = instance;");
        builder.AppendLine("            Handle = handle;");
        builder.AppendLine("            Value = value;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public object Instance { get; }");
        builder.AppendLine();
        builder.AppendLine("        public nint Handle { get; }");
        builder.AppendLine();
        builder.AppendLine("        public global::BunSharp.BunValue Value { get; }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private sealed class StableIdentityEntry");
        builder.AppendLine("    {");
        builder.AppendLine("        public StableIdentityEntry(object source, global::BunSharp.BunValue value, global::System.IDisposable? ownedReference)");
        builder.AppendLine("        {");
        builder.AppendLine("            Source = source;");
        builder.AppendLine("            Value = value;");
        builder.AppendLine("            OwnedReference = ownedReference;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public object Source { get; }");
        builder.AppendLine();
        builder.AppendLine("        public global::BunSharp.BunValue Value { get; }");
        builder.AppendLine();
        builder.AppendLine("        public global::System.IDisposable? OwnedReference { get; }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private sealed class RegistrationState");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly HashSet<nint> _trackedHandles = new();");
        builder.AppendLine("        private readonly Dictionary<object, ManagedWrapperEntry> _wrappersByInstance = new(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
        builder.AppendLine("        private readonly Dictionary<nint, ManagedWrapperEntry> _wrappersByHandle = new();");
        builder.AppendLine("        private readonly Dictionary<nint, Dictionary<int, StableIdentityEntry>> _stableIdentityEntries = new();");
        builder.AppendLine();
        builder.AppendLine("        public RegistrationState(global::BunSharp.BunClass @class, global::BunSharp.BunValue constructor, global::BunSharp.BunClassPersistentFinalizer releaseHandleFinalizer, nint contextHandle)");
        builder.AppendLine("        {");
        builder.AppendLine("            Class = @class;");
        builder.AppendLine("            Constructor = constructor;");
        builder.AppendLine("            ReleaseHandleFinalizer = releaseHandleFinalizer;");
        builder.AppendLine("            ContextHandle = contextHandle;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public global::BunSharp.BunClass Class { get; }");
        builder.AppendLine();
        builder.AppendLine("        public global::BunSharp.BunValue Constructor { get; }");
        builder.AppendLine();
        builder.AppendLine("        public global::BunSharp.BunClassPersistentFinalizer ReleaseHandleFinalizer { get; }");
        builder.AppendLine();
        builder.AppendLine("        public nint ContextHandle { get; }");
        builder.AppendLine();
        builder.AppendLine("        public bool TryGetWrapper(object instance, out global::BunSharp.BunValue value)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (_wrappersByInstance.TryGetValue(instance, out var entry))");
        builder.AppendLine("            {");
        builder.AppendLine("                value = entry.Value;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            value = default;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void TrackWrapper(object instance, nint handle, global::BunSharp.BunValue value)");
        builder.AppendLine("        {");
        builder.AppendLine("            _trackedHandles.Add(handle);");
        builder.AppendLine("            var entry = new ManagedWrapperEntry(instance, handle, value);");
        builder.AppendLine("            _wrappersByInstance[instance] = entry;");
        builder.AppendLine("            _wrappersByHandle[handle] = entry;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void TrackHandle(nint handle)");
        builder.AppendLine("        {");
        builder.AppendLine("            _trackedHandles.Add(handle);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public bool TryGetStableIdentityValue(nint handle, int memberSlot, object source, out global::BunSharp.BunValue value)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (_stableIdentityEntries.TryGetValue(handle, out var members) && members.TryGetValue(memberSlot, out var entry) && global::System.Object.ReferenceEquals(entry.Source, source))");
        builder.AppendLine("            {");
        builder.AppendLine("                value = entry.Value;");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            value = default;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void CacheStableIdentityValue(nint handle, int memberSlot, object source, global::BunSharp.BunValue value, global::System.IDisposable? ownedReference = null)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!_stableIdentityEntries.TryGetValue(handle, out var members))");
        builder.AppendLine("            {");
        builder.AppendLine("                members = new Dictionary<int, StableIdentityEntry>();");
        builder.AppendLine("                _stableIdentityEntries[handle] = members;");
        builder.AppendLine("            }");
        builder.AppendLine("            var hadExisting = members.TryGetValue(memberSlot, out var existingEntry);");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                global::BunSharp.Interop.BunNative.Protect(ContextHandle, value);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                ownedReference?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                members[memberSlot] = new StableIdentityEntry(source, value, ownedReference);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                global::BunSharp.Interop.BunNative.Unprotect(ContextHandle, value);");
        builder.AppendLine("                ownedReference?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            if (hadExisting)");
        builder.AppendLine("            {");
        builder.AppendLine("                var existing = existingEntry!;");
        builder.AppendLine("                global::BunSharp.Interop.BunNative.Unprotect(ContextHandle, existing.Value);");
        builder.AppendLine("                existing.OwnedReference?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void ClearStableIdentityValue(nint handle, int memberSlot)");
        builder.AppendLine("        {");
        builder.AppendLine("            ClearStableIdentityValueCore(handle, memberSlot);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void ReleaseStableIdentityValuesForHandle(nint handle)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!_stableIdentityEntries.Remove(handle, out var members))");
        builder.AppendLine("            {");
        builder.AppendLine("                return;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            global::System.Collections.Generic.List<global::System.Exception>? errors = null;");
        builder.AppendLine("            foreach (var entry in members.Values)");
        builder.AppendLine("            {");
        builder.AppendLine("                try");
        builder.AppendLine("                {");
        builder.AppendLine("                    global::BunSharp.Interop.BunNative.Unprotect(ContextHandle, entry.Value);");
        builder.AppendLine("                    entry.OwnedReference?.Dispose();");
        builder.AppendLine("                }");
        builder.AppendLine("                catch (global::System.Exception ex)");
        builder.AppendLine("                {");
        builder.AppendLine("                    errors ??= new();");
        builder.AppendLine("                    errors.Add(ex);");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            if (errors is not null)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.AggregateException(errors);");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public bool UntrackHandle(nint handle)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!_trackedHandles.Remove(handle))");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (_wrappersByHandle.Remove(handle, out var entry))");
        builder.AppendLine("            {");
        builder.AppendLine("                _wrappersByInstance.Remove(entry.Instance);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public nint[] DrainTrackedHandles()");
        builder.AppendLine("        {");
        builder.AppendLine("            if (_trackedHandles.Count == 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                _wrappersByHandle.Clear();");
        builder.AppendLine("                _wrappersByInstance.Clear();");
        builder.AppendLine("                return global::System.Array.Empty<nint>();");
        builder.AppendLine("            }");
        builder.AppendLine("            var handles = _trackedHandles.ToArray();");
        builder.AppendLine("            _trackedHandles.Clear();");
        builder.AppendLine("            _wrappersByHandle.Clear();");
        builder.AppendLine("            _wrappersByInstance.Clear();");
        builder.AppendLine("            return handles;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void DisposeReleaseHandleFinalizer()");
        builder.AppendLine("        {");
        builder.AppendLine("            ReleaseHandleFinalizer.Dispose();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private void ClearStableIdentityValueCore(nint handle, int memberSlot)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!_stableIdentityEntries.TryGetValue(handle, out var members) || !members.Remove(memberSlot, out var entry))");
        builder.AppendLine("            {");
        builder.AppendLine("                return;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            global::System.Collections.Generic.List<global::System.Exception>? errors = null;");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                global::BunSharp.Interop.BunNative.Unprotect(ContextHandle, entry.Value);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception ex)");
        builder.AppendLine("            {");
        builder.AppendLine("                errors ??= new();");
        builder.AppendLine("                errors.Add(ex);");
        builder.AppendLine("            }");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                entry.OwnedReference?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception ex)");
        builder.AppendLine("            {");
        builder.AppendLine("                errors ??= new();");
        builder.AppendLine("                errors.Add(ex);");
        builder.AppendLine("            }");
        builder.AppendLine("            if (members.Count == 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                _stableIdentityEntries.Remove(handle);");
        builder.AppendLine("            }");
        builder.AppendLine("            if (errors is not null)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.AggregateException(errors);");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static readonly object SyncRoot = new();");
        builder.AppendLine("    private static readonly Dictionary<nint, RegistrationState> Registrations = new();");
        builder.AppendLine("    [ThreadStatic]");
        builder.AppendLine("    private static nint t_cachedContextHandle;");
        builder.AppendLine("    [ThreadStatic]");
        builder.AppendLine("    private static RegistrationState? t_cachedRegistration;");
        builder.AppendLine();
        builder.AppendLine("    public static global::BunSharp.BunValue Register(global::BunSharp.BunContext context)");
        builder.AppendLine("    {");
        builder.AppendLine("        ArgumentNullException.ThrowIfNull(context);");
        builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: true);");
        builder.AppendLine("        return registration.Constructor;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static RegistrationState CacheRegistration(nint contextHandle, RegistrationState registration)");
        builder.AppendLine("    {");
        builder.AppendLine("        t_cachedContextHandle = contextHandle;");
        builder.AppendLine("        t_cachedRegistration = registration;");
        builder.AppendLine("        return registration;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static bool TryGetCachedRegistration(nint contextHandle, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RegistrationState? registration)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (t_cachedContextHandle == contextHandle && t_cachedRegistration is not null)");
        builder.AppendLine("        {");
        builder.AppendLine("            registration = t_cachedRegistration;");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        registration = null!;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static bool TryGetExistingRegistration(global::BunSharp.BunContext context, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RegistrationState? registration)");
        builder.AppendLine("    {");
        builder.AppendLine("        var contextHandle = context.Handle;");
        builder.AppendLine("        if (TryGetCachedRegistration(contextHandle, out registration))");
        builder.AppendLine("        {");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        lock (SyncRoot)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (Registrations.TryGetValue(contextHandle, out var existing))");
        builder.AppendLine("            {");
        builder.AppendLine("                registration = CacheRegistration(contextHandle, existing);");
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        registration = null!;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static RegistrationState GetOrCreate(global::BunSharp.BunContext context, bool publishGlobal)");
        builder.AppendLine("    {");
        builder.AppendLine("        var contextHandle = context.Handle;");
        builder.AppendLine("        if (TryGetCachedRegistration(contextHandle, out var cached))");
        builder.AppendLine("        {");
        builder.AppendLine("            if (publishGlobal)");
        builder.AppendLine("            {");
        builder.Append("                PublishGlobal(context, cached.Constructor, ");
        builder.Append(CSharpLiteral(model.ExportName));
        builder.AppendLine(");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return cached;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        lock (SyncRoot)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (Registrations.TryGetValue(contextHandle, out var existing))");
        builder.AppendLine("            {");
        builder.AppendLine("                if (publishGlobal)");
        builder.AppendLine("                {");
        builder.Append("                    PublishGlobal(context, existing.Constructor, ");
        builder.Append(CSharpLiteral(model.ExportName));
        builder.AppendLine(");");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                return CacheRegistration(contextHandle, existing);");
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
        builder.AppendLine("            var releaseHandleFinalizer = @class.CreatePersistentFinalizer(ReleaseHandle, contextHandle);");
        builder.AppendLine("            var registration = new RegistrationState(@class, constructor, releaseHandleFinalizer, contextHandle);");
        builder.AppendLine("            Registrations.Add(contextHandle, registration);");
        builder.AppendLine("            context.RegisterPreDestroyCleanup(() =>");
        builder.AppendLine("            {");
        builder.AppendLine("                lock (SyncRoot)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (Registrations.Remove(contextHandle, out var removed))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        global::System.Collections.Generic.List<global::System.Exception>? errors = null;");
        builder.AppendLine("                        try");
        builder.AppendLine("                        {");
        builder.AppendLine("                            ReleaseStaticReferences();");
        builder.AppendLine("                        }");
        builder.AppendLine("                        catch (global::System.Exception ex)");
        builder.AppendLine("                        {");
        builder.AppendLine("                            errors ??= new();");
        builder.AppendLine("                            errors.Add(ex);");
        builder.AppendLine("                        }");
        builder.AppendLine("                        try");
        builder.AppendLine("                        {");
        builder.AppendLine("                            removed.ReleaseStableIdentityValuesForHandle(0);");
        builder.AppendLine("                        }");
        builder.AppendLine("                        catch (global::System.Exception ex)");
        builder.AppendLine("                        {");
        builder.AppendLine("                            errors ??= new();");
        builder.AppendLine("                            errors.Add(ex);");
        builder.AppendLine("                        }");
        builder.AppendLine("                        try");
        builder.AppendLine("                        {");
        builder.AppendLine("                            global::System.Collections.Generic.List<global::System.Exception>? trackedErrors = null;");
        builder.AppendLine("                            var trackedHandles = removed.DrainTrackedHandles();");
        builder.AppendLine("                            foreach (var trackedHandle in trackedHandles)");
        builder.AppendLine("                            {");
        builder.AppendLine("                                var handle = GCHandle.FromIntPtr(trackedHandle);");
        builder.AppendLine("                                try");
        builder.AppendLine("                                {");
        builder.Append("                                    ReleaseTrackedInstance(removed, trackedHandle, (");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("?)handle.Target);");
        builder.AppendLine("                                }");
        builder.AppendLine("                                catch (global::System.Exception ex)");
        builder.AppendLine("                                {");
        builder.AppendLine("                                    trackedErrors ??= new();");
        builder.AppendLine("                                    trackedErrors.Add(ex);");
        builder.AppendLine("                                }");
        builder.AppendLine("                                finally");
        builder.AppendLine("                                {");
        builder.AppendLine("                                    handle.Free();");
        builder.AppendLine("                                }");
        builder.AppendLine("                            }");
        builder.AppendLine("                            if (trackedErrors is not null)");
        builder.AppendLine("                            {");
        builder.AppendLine("                                throw new global::System.AggregateException(trackedErrors);");
        builder.AppendLine("                            }");
        builder.AppendLine("                        }");
        builder.AppendLine("                        catch (global::System.Exception ex)");
        builder.AppendLine("                        {");
        builder.AppendLine("                            errors ??= new();");
        builder.AppendLine("                            errors.Add(ex);");
        builder.AppendLine("                        }");
        builder.AppendLine("                        try");
        builder.AppendLine("                        {");
        builder.AppendLine("                            removed.DisposeReleaseHandleFinalizer();");
        builder.AppendLine("                        }");
        builder.AppendLine("                        catch (global::System.Exception ex)");
        builder.AppendLine("                        {");
        builder.AppendLine("                            errors ??= new();");
        builder.AppendLine("                            errors.Add(ex);");
        builder.AppendLine("                        }");
        builder.AppendLine("                        if (errors is not null)");
        builder.AppendLine("                        {");
        builder.AppendLine("                            throw new global::System.AggregateException(errors);");
        builder.AppendLine("                        }");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                if (t_cachedContextHandle == contextHandle)");
        builder.AppendLine("                {");
        builder.AppendLine("                    t_cachedContextHandle = 0;");
        builder.AppendLine("                    t_cachedRegistration = null;");
        builder.AppendLine("                }");
        builder.AppendLine("            });");
        builder.AppendLine("            if (publishGlobal)");
        builder.AppendLine("            {");
        builder.Append("                PublishGlobal(context, constructor, ");
        builder.Append(CSharpLiteral(model.ExportName));
        builder.AppendLine(");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return CacheRegistration(contextHandle, registration);");
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
        builder.Append("    internal static global::BunSharp.BunValue WrapManaged(global::BunSharp.BunContext context, ");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("? instance)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (instance is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            return global::BunSharp.BunValue.Null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
        builder.AppendLine("        if (registration.TryGetWrapper(instance, out var cached))");
        builder.AppendLine("        {");
        builder.AppendLine("            return cached;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var handle = GCHandle.Alloc(instance, GCHandleType.Normal);");
        builder.AppendLine("        var handlePtr = GCHandle.ToIntPtr(handle);");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            var value = registration.Class.CreateInstance(handlePtr, registration.ReleaseHandleFinalizer);");
        builder.AppendLine("            registration.TrackWrapper(instance, handlePtr, value);");
        builder.AppendLine("            return value;");
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            handle.Free();");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    internal static bool TryGetManagedWrapper(global::BunSharp.BunContext context, ");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine(" instance, out global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        ArgumentNullException.ThrowIfNull(context);");
        builder.AppendLine("        ArgumentNullException.ThrowIfNull(instance);");
        builder.AppendLine();
        builder.AppendLine("        if (!TryGetExistingRegistration(context, out var registration))");
        builder.AppendLine("        {");
        builder.AppendLine("            value = default;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return registration.TryGetWrapper(instance, out value);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    internal static bool TryUnwrapManaged(global::BunSharp.BunContext context, global::BunSharp.BunValue value, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("? result)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            result = null;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (!TryGetExistingRegistration(context, out var registration))");
        builder.AppendLine("        {");
        builder.AppendLine("            result = null;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var nativePtr = registration.Class.Unwrap(value);");
        builder.AppendLine("        if (nativePtr == 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            result = null;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var handle = GCHandle.FromIntPtr(nativePtr);");
        builder.Append("        result = (");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("?)handle.Target;");
        builder.AppendLine("        return result is not null;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    internal static ");
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
        builder.AppendLine("    internal static bool DisposeExportedInstance(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
        builder.AppendLine("        return registration.Class.DisposeInstance(value);");
        builder.AppendLine("    }");
        builder.AppendLine();
        AppendReleaseReferenceMethods(builder, model);
        builder.AppendLine();
        builder.AppendLine("    private static void ReleaseHandle(nint nativePtr, nint userdata)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (nativePtr == 0) return;");
        builder.AppendLine("        if (TryGetCachedRegistration(userdata, out var cached) && cached.UntrackHandle(nativePtr))");
        builder.AppendLine("        {");
        builder.AppendLine("            var cachedHandle = GCHandle.FromIntPtr(nativePtr);");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                ReleaseTrackedInstance(cached, nativePtr, (");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("?)cachedHandle.Target);");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                cachedHandle.Free();");
        builder.AppendLine("            }");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        RegistrationState reg;");
        builder.AppendLine("        lock (SyncRoot)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!Registrations.TryGetValue(userdata, out var existing) || !existing.UntrackHandle(nativePtr))");
        builder.AppendLine("            {");
        builder.AppendLine("                return;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            CacheRegistration(userdata, existing);");
        builder.AppendLine("            reg = existing;");
        builder.AppendLine("        }");
        builder.AppendLine("        var handle = GCHandle.FromIntPtr(nativePtr);");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.Append("            ReleaseTrackedInstance(reg, nativePtr, (");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("?)handle.Target);");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        builder.AppendLine("            handle.Free();");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();

        var stableMemberSlots = CreateStableMemberSlots(model);

        foreach (var method in model.InstanceMethods)
        {
            AppendInstanceMethod(builder, model, method, stableMemberSlots);
        }

        foreach (var method in model.StaticMethods)
        {
            AppendStaticMethod(builder, model, method, stableMemberSlots);
        }

        foreach (var property in model.InstanceProperties)
        {
            AppendInstanceProperty(builder, model, property, stableMemberSlots);
        }

        foreach (var property in model.StaticProperties)
        {
            AppendStaticProperty(builder, model, property, stableMemberSlots);
        }

        foreach (var property in model.InstanceProperties.Concat(model.StaticProperties).Where(static property => property.PropertyType.IsDelegate))
        {
            AppendDelegatePropertyHelpers(builder, model, property);
        }

        foreach (var method in model.InstanceMethods.Concat(model.StaticMethods).Where(static method => method.ReturnType.IsDelegate))
        {
            AppendDelegateMethodHelpers(builder, model, method);
        }

        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendReleaseReferenceMethods(StringBuilder builder, ExportedTypeModel model)
    {
        var instanceDisposableProperties = model.InstanceProperties.Where(static property => property.PropertyType.ShouldDisposePreviousValueOnReplacement).ToList();
        var staticDisposableProperties = model.StaticProperties.Where(static property => property.PropertyType.ShouldDisposePreviousValueOnReplacement).ToList();

        builder.Append("    private static void ReleaseInstanceReferences(");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("? target)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (target is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");

        if (instanceDisposableProperties.Count > 0)
        {
            builder.AppendLine("        global::System.Collections.Generic.List<global::System.Exception>? errors = null;");

            foreach (var property in instanceDisposableProperties)
            {
                builder.AppendLine("        try");
                builder.AppendLine("        {");
                builder.Append("            if (target.");
                builder.Append(property.MemberName);
                builder.AppendLine(" is not null)");
                builder.AppendLine("            {");
                builder.Append("                var value = target.");
                builder.Append(property.MemberName);
                builder.AppendLine(";");
                if (property.HasSetter)
                {
                    builder.Append("                target.");
                    builder.Append(property.MemberName);
                    builder.AppendLine(" = default!;");
                }
                builder.AppendLine("                value.Dispose();");
                builder.AppendLine("            }");
                builder.AppendLine("        }");
                builder.AppendLine("        catch (global::System.Exception ex)");
                builder.AppendLine("        {");
                builder.AppendLine("            errors ??= new();");
                builder.AppendLine("            errors.Add(ex);");
                builder.AppendLine("        }");
            }

            builder.AppendLine("        if (errors is not null)");
            builder.AppendLine("        {");
            builder.AppendLine("            throw new global::System.AggregateException(errors);");
            builder.AppendLine("        }");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    private static void DisposeManagedInstance(");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("? target)");
        builder.AppendLine("    {");
        builder.AppendLine("        if ((object?)target is global::System.IDisposable disposable)");
        builder.AppendLine("        {");
        builder.AppendLine("            disposable.Dispose();");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    private static void ReleaseTrackedInstance(RegistrationState registration, nint nativePtr, ");
        builder.Append(model.FullyQualifiedTypeName);
        builder.AppendLine("? target)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.Collections.Generic.List<global::System.Exception>? errors = null;");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            registration.ReleaseStableIdentityValuesForHandle(nativePtr);");
        builder.AppendLine("        }");
        builder.AppendLine("        catch (global::System.Exception ex)");
        builder.AppendLine("        {");
        builder.AppendLine("            errors ??= new();");
        builder.AppendLine("            errors.Add(ex);");
        builder.AppendLine("        }");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            DisposeManagedInstance(target);");
        builder.AppendLine("        }");
        builder.AppendLine("        catch (global::System.Exception ex)");
        builder.AppendLine("        {");
        builder.AppendLine("            errors ??= new();");
        builder.AppendLine("            errors.Add(ex);");
        builder.AppendLine("        }");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            ReleaseInstanceReferences(target);");
        builder.AppendLine("        }");
        builder.AppendLine("        catch (global::System.Exception ex)");
        builder.AppendLine("        {");
        builder.AppendLine("            errors ??= new();");
        builder.AppendLine("            errors.Add(ex);");
        builder.AppendLine("        }");
        builder.AppendLine("        if (errors is not null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.AggregateException(errors);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static void ReleaseStaticReferences()");
        builder.AppendLine("    {");

        if (staticDisposableProperties.Count > 0)
        {
            builder.AppendLine("        global::System.Collections.Generic.List<global::System.Exception>? errors = null;");

            foreach (var property in staticDisposableProperties)
            {
                builder.AppendLine("        try");
                builder.AppendLine("        {");
                builder.Append("            if (");
                builder.Append(model.FullyQualifiedTypeName);
                builder.Append('.');
                builder.Append(property.MemberName);
                builder.AppendLine(" is not null)");
                builder.AppendLine("            {");
                builder.Append("                var value = ");
                builder.Append(model.FullyQualifiedTypeName);
                builder.Append('.');
                builder.Append(property.MemberName);
                builder.AppendLine(";");
                if (property.HasSetter)
                {
                    builder.Append("                ");
                    builder.Append(model.FullyQualifiedTypeName);
                    builder.Append('.');
                    builder.Append(property.MemberName);
                    builder.AppendLine(" = default!;");
                }
                builder.AppendLine("                value.Dispose();");
                builder.AppendLine("            }");
                builder.AppendLine("        }");
                builder.AppendLine("        catch (global::System.Exception ex)");
                builder.AppendLine("        {");
                builder.AppendLine("            errors ??= new();");
                builder.AppendLine("            errors.Add(ex);");
                builder.AppendLine("        }");
            }

            builder.AppendLine("        if (errors is not null)");
            builder.AppendLine("        {");
            builder.AppendLine("            throw new global::System.AggregateException(errors);");
            builder.AppendLine("        }");
        }

        builder.AppendLine("    }");
    }

    private static Dictionary<string, int> CreateStableMemberSlots(ExportedTypeModel model)
    {
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        var nextSlot = 0;

        void Add(string memberName)
        {
            if (!slots.ContainsKey(memberName))
            {
                slots.Add(memberName, nextSlot++);
            }
        }

        foreach (var property in model.InstanceProperties.Concat(model.StaticProperties))
        {
            if (property.PropertyType.IsDelegate || property.Stable)
            {
                Add(property.MemberName);
            }
        }

        foreach (var method in model.InstanceMethods.Concat(model.StaticMethods))
        {
            if (method.Stable)
            {
                Add(method.MemberName);
            }
        }

        return slots;
    }

    private static int GetStableMemberSlot(IReadOnlyDictionary<string, int> stableMemberSlots, string memberName)
    {
        return stableMemberSlots[memberName];
    }

    private static void AppendInstanceMethod(StringBuilder builder, ExportedTypeModel model, ExportedMethodModel method, IReadOnlyDictionary<string, int> stableMemberSlots)
    {
        var stableMemberSlot = method.Stable ? GetStableMemberSlot(stableMemberSlots, method.MemberName) : -1;
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
        if (method.Stable)
        {
            builder.Append("        var result = target.");
            builder.Append(method.MemberName);
            builder.Append('(');
            builder.Append(string.Join(", ", method.Parameters.Select(static parameter => parameter.LocalName)));
            builder.AppendLine(");");
            builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
            builder.AppendLine("        if (result is null)");
            builder.AppendLine("        {");
            builder.Append("            registration.ClearStableIdentityValue(nativePtr, ");
            builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(");");
            builder.AppendLine("            return global::BunSharp.BunValue.Null;");
            builder.AppendLine("        }");
            builder.Append("        if (registration.TryGetStableIdentityValue(nativePtr, ");
            builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(", result, out var cached))");
            builder.AppendLine("        {");
            builder.AppendLine("            return cached;");
            builder.AppendLine("        }");
            builder.Append("        var value = ");
            builder.Append(method.ReturnType.IsDelegate ? $"{method.DelegateFunctionHelperName}(context, result)" : ConvertToBunValue(method.ReturnType, "result", "context"));
            builder.AppendLine(";");
            builder.Append("        registration.CacheStableIdentityValue(nativePtr, ");
            builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(", result, value);");
            builder.AppendLine("        return value;");
        }
        else
        {
            AppendInvocation(builder, method.ReturnType, $"target.{method.MemberName}({string.Join(", ", method.Parameters.Select(static parameter => parameter.LocalName))})", "        ");
        }
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendInstanceProperty(StringBuilder builder, ExportedTypeModel model, ExportedPropertyModel property, IReadOnlyDictionary<string, int> stableMemberSlots)
    {
        var stableMemberSlot = (property.PropertyType.IsDelegate || property.Stable)
            ? GetStableMemberSlot(stableMemberSlots, property.MemberName)
            : -1;
        if (property.HasGetter)
        {
            builder.Append("    private static global::BunSharp.BunValue ");
            builder.Append(property.GetterName);
            builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue thisValue, nint nativePtr, nint userdata)");
            builder.AppendLine("    {");
            builder.Append("        var target = (");
            builder.Append(model.FullyQualifiedTypeName);
            builder.AppendLine(")GCHandle.FromIntPtr(nativePtr).Target!;");
            if (property.PropertyType.IsDelegate)
            {
                builder.Append("        var source = target.");
                builder.Append(property.MemberName);
                builder.AppendLine(";");
                builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
                builder.AppendLine("        if (source is null)");
                builder.AppendLine("        {");
                builder.Append("            registration.ClearStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(");");
                builder.AppendLine("            return global::BunSharp.BunValue.Null;");
                builder.AppendLine("        }");
                builder.Append("        if (registration.TryGetStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", source, out var cached))");
                builder.AppendLine("        {");
                builder.AppendLine("            return cached;");
                builder.AppendLine("        }");
                builder.Append("        var result = ");
                builder.Append(property.DelegateFunctionHelperName);
                builder.AppendLine("(context, source);");
                builder.Append("        registration.CacheStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", source, result);");
                builder.AppendLine("        return result;");
            }
            else if (property.Stable)
            {
                builder.Append("        var source = target.");
                builder.Append(property.MemberName);
                builder.AppendLine(";");
                builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
                builder.AppendLine("        if (source is null)");
                builder.AppendLine("        {");
                builder.Append("            registration.ClearStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(");");
                builder.AppendLine("            return global::BunSharp.BunValue.Null;");
                builder.AppendLine("        }");
                builder.Append("        if (registration.TryGetStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", source, out var cached))");
                builder.AppendLine("        {");
                builder.AppendLine("            return cached;");
                builder.AppendLine("        }");
                builder.Append("        var result = ");
                builder.Append(ConvertToBunValue(property.PropertyType, "source", "context"));
                builder.AppendLine(";");
                builder.Append("        registration.CacheStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", source, result);");
                builder.AppendLine("        return result;");
            }
            else
            {
                AppendInvocation(builder, property.PropertyType, $"target.{property.MemberName}", "        ");
            }
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
            if (property.PropertyType.IsDelegate)
            {
                builder.Append("        var binding = ");
                builder.Append(property.DelegateBindingHelperName);
                builder.AppendLine("(context, value);");
                builder.Append("        var nextValue = ");
                builder.Append(property.PropertyType.IsNullableReferenceType ? "binding.Value" : "binding.Value!");
                builder.AppendLine(";");
                builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
                builder.AppendLine("        try");
                builder.AppendLine("        {");
                builder.Append("            target.");
                builder.Append(property.MemberName);
                builder.AppendLine(" = nextValue;");
                builder.AppendLine("        }");
                builder.AppendLine("        catch");
                builder.AppendLine("        {");
                builder.AppendLine("            __JSExportCommon.DisposeReference(binding.OwnedReference);");
                builder.AppendLine("            throw;");
                builder.AppendLine("        }");
                builder.Append("        registration.ClearStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(");");
                builder.AppendLine("        if (binding.HasCachedValue)");
                builder.AppendLine("        {");
                builder.Append("            registration.CacheStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", nextValue!, binding.CachedValue, binding.OwnedReference);");
                builder.AppendLine("        }");
            }
            else if (property.PropertyType.ShouldDisposePreviousValueOnReplacement)
            {
                builder.Append("        var currentValue = target.");
                builder.Append(property.MemberName);
                builder.AppendLine(";");
                builder.Append("        var nextValue = ");
                builder.Append(ConvertFromBunValue(property.PropertyType, "value", "context"));
                builder.AppendLine(";");
                builder.AppendLine("        try");
                builder.AppendLine("        {");
                builder.Append("            target.");
                builder.Append(property.MemberName);
                builder.AppendLine(" = nextValue;");
                builder.AppendLine("        }");
                builder.AppendLine("        catch");
                builder.AppendLine("        {");
                builder.AppendLine("            __JSExportCommon.DisposeFailedReferenceAssignment(currentValue, nextValue);");
                builder.AppendLine("            throw;");
                builder.AppendLine("        }");
                builder.AppendLine("        __JSExportCommon.DisposeReplacedReference(currentValue, nextValue);");
            }
            else if (property.Stable)
            {
                builder.Append("        var nextValue = ");
                builder.Append(ConvertFromBunValue(property.PropertyType, "value", "context"));
                builder.AppendLine(";");
                builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
                builder.AppendLine("        target.");
                builder.Append(property.MemberName);
                builder.AppendLine(" = nextValue;");
                builder.Append("        registration.ClearStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(");");
                builder.AppendLine("        if (nextValue is not null)");
                builder.AppendLine("        {");
                builder.Append("            registration.CacheStableIdentityValue(nativePtr, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", nextValue, value);");
                builder.AppendLine("        }");
            }
            else
            {
                builder.Append("        target.");
                builder.Append(property.MemberName);
                builder.Append(" = ");
                builder.Append(ConvertFromBunValue(property.PropertyType, "value", "context"));
                builder.AppendLine(";");
            }
            builder.AppendLine("    }");
            builder.AppendLine();
        }
    }

    private static void AppendStaticProperty(StringBuilder builder, ExportedTypeModel model, ExportedPropertyModel property, IReadOnlyDictionary<string, int> stableMemberSlots)
    {
        var stableMemberSlot = (property.PropertyType.IsDelegate || property.Stable)
            ? GetStableMemberSlot(stableMemberSlots, property.MemberName)
            : -1;
        if (property.HasGetter)
        {
            builder.Append("    private static global::BunSharp.BunValue ");
            builder.Append(property.GetterName);
            builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue thisValue, nint userdata)");
            builder.AppendLine("    {");
            builder.AppendLine("        _ = thisValue;");
            builder.AppendLine("        _ = userdata;");
            if (property.PropertyType.IsDelegate)
            {
                builder.Append("        var source = ");
                builder.Append(model.FullyQualifiedTypeName);
                builder.Append('.');
                builder.Append(property.MemberName);
                builder.AppendLine(";");
                builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
                builder.AppendLine("        if (source is null)");
                builder.AppendLine("        {");
                builder.Append("            registration.ClearStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(");");
                builder.AppendLine("            return global::BunSharp.BunValue.Null;");
                builder.AppendLine("        }");
                builder.Append("        if (registration.TryGetStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", source, out var cached))");
                builder.AppendLine("        {");
                builder.AppendLine("            return cached;");
                builder.AppendLine("        }");
                builder.Append("        var result = ");
                builder.Append(property.DelegateFunctionHelperName);
                builder.AppendLine("(context, source);");
                builder.Append("        registration.CacheStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", source, result);");
                builder.AppendLine("        return result;");
            }
            else if (property.Stable)
            {
                builder.Append("        var source = ");
                builder.Append(model.FullyQualifiedTypeName);
                builder.Append('.');
                builder.Append(property.MemberName);
                builder.AppendLine(";");
                builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
                builder.AppendLine("        if (source is null)");
                builder.AppendLine("        {");
                builder.Append("            registration.ClearStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(");");
                builder.AppendLine("            return global::BunSharp.BunValue.Null;");
                builder.AppendLine("        }");
                builder.Append("        if (registration.TryGetStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", source, out var cached))");
                builder.AppendLine("        {");
                builder.AppendLine("            return cached;");
                builder.AppendLine("        }");
                builder.Append("        var result = ");
                builder.Append(ConvertToBunValue(property.PropertyType, "source", "context"));
                builder.AppendLine(";");
                builder.Append("        registration.CacheStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", source, result);");
                builder.AppendLine("        return result;");
            }
            else
            {
                AppendInvocation(builder, property.PropertyType, $"{model.FullyQualifiedTypeName}.{property.MemberName}", "        ");
            }
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
            if (property.PropertyType.IsDelegate)
            {
                builder.Append("        var binding = ");
                builder.Append(property.DelegateBindingHelperName);
                builder.AppendLine("(context, value);");
                builder.Append("        var nextValue = ");
                builder.Append(property.PropertyType.IsNullableReferenceType ? "binding.Value" : "binding.Value!");
                builder.AppendLine(";");
                builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
                builder.AppendLine("        try");
                builder.AppendLine("        {");
                builder.Append("            ");
                builder.Append(model.FullyQualifiedTypeName);
                builder.Append('.');
                builder.Append(property.MemberName);
                builder.AppendLine(" = nextValue;");
                builder.AppendLine("        }");
                builder.AppendLine("        catch");
                builder.AppendLine("        {");
                builder.AppendLine("            __JSExportCommon.DisposeReference(binding.OwnedReference);");
                builder.AppendLine("            throw;");
                builder.AppendLine("        }");
                builder.Append("        registration.ClearStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(");");
                builder.AppendLine("        if (binding.HasCachedValue)");
                builder.AppendLine("        {");
                builder.Append("            registration.CacheStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", nextValue!, binding.CachedValue, binding.OwnedReference);");
                builder.AppendLine("        }");
            }
            else if (property.PropertyType.ShouldDisposePreviousValueOnReplacement)
            {
                builder.Append("        var currentValue = ");
                builder.Append(model.FullyQualifiedTypeName);
                builder.Append('.');
                builder.Append(property.MemberName);
                builder.AppendLine(";");
                builder.Append("        var nextValue = ");
                builder.Append(ConvertFromBunValue(property.PropertyType, "value", "context"));
                builder.AppendLine(";");
                builder.AppendLine("        try");
                builder.AppendLine("        {");
                builder.Append("            ");
                builder.Append(model.FullyQualifiedTypeName);
                builder.Append('.');
                builder.Append(property.MemberName);
                builder.AppendLine(" = nextValue;");
                builder.AppendLine("        }");
                builder.AppendLine("        catch");
                builder.AppendLine("        {");
                builder.AppendLine("            __JSExportCommon.DisposeFailedReferenceAssignment(currentValue, nextValue);");
                builder.AppendLine("            throw;");
                builder.AppendLine("        }");
                builder.AppendLine("        __JSExportCommon.DisposeReplacedReference(currentValue, nextValue);");
            }
            else if (property.Stable)
            {
                builder.Append("        var nextValue = ");
                builder.Append(ConvertFromBunValue(property.PropertyType, "value", "context"));
                builder.AppendLine(";");
                builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
                builder.Append(model.FullyQualifiedTypeName);
                builder.Append('.');
                builder.Append(property.MemberName);
                builder.AppendLine(" = nextValue;");
                builder.Append("        registration.ClearStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(");");
                builder.AppendLine("        if (nextValue is not null)");
                builder.AppendLine("        {");
                builder.Append("            registration.CacheStableIdentityValue(0, ");
                builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(", nextValue, value);");
                builder.AppendLine("        }");
            }
            else
            {
                builder.Append(model.FullyQualifiedTypeName);
                builder.Append('.');
                builder.Append(property.MemberName);
                builder.Append(" = ");
                builder.Append(ConvertFromBunValue(property.PropertyType, "value", "context"));
                builder.AppendLine(";");
            }
            builder.AppendLine("    }");
            builder.AppendLine();
        }
    }

    private static void AppendStaticMethod(StringBuilder builder, ExportedTypeModel model, ExportedMethodModel method, IReadOnlyDictionary<string, int> stableMemberSlots)
    {
        var stableMemberSlot = method.Stable ? GetStableMemberSlot(stableMemberSlots, method.MemberName) : -1;
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
        if (method.Stable)
        {
            builder.Append("        var result = ");
            builder.Append(model.FullyQualifiedTypeName);
            builder.Append('.');
            builder.Append(method.MemberName);
            builder.Append('(');
            builder.Append(string.Join(", ", method.Parameters.Select(static parameter => parameter.LocalName)));
            builder.AppendLine(");");
            builder.AppendLine("        var registration = GetOrCreate(context, publishGlobal: false);");
            builder.AppendLine("        if (result is null)");
            builder.AppendLine("        {");
            builder.Append("            registration.ClearStableIdentityValue(0, ");
            builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(");");
            builder.AppendLine("            return global::BunSharp.BunValue.Null;");
            builder.AppendLine("        }");
            builder.Append("        if (registration.TryGetStableIdentityValue(0, ");
            builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(", result, out var cached))");
            builder.AppendLine("        {");
            builder.AppendLine("            return cached;");
            builder.AppendLine("        }");
            builder.Append("        var value = ");
            builder.Append(method.ReturnType.IsDelegate ? $"{method.DelegateFunctionHelperName}(context, result)" : ConvertToBunValue(method.ReturnType, "result", "context"));
            builder.AppendLine(";");
            builder.Append("        registration.CacheStableIdentityValue(0, ");
            builder.Append(stableMemberSlot.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(", result, value);");
            builder.AppendLine("        return value;");
        }
        else
        {
            AppendInvocation(builder, method.ReturnType, $"{model.FullyQualifiedTypeName}.{method.MemberName}({string.Join(", ", method.Parameters.Select(static parameter => parameter.LocalName))})", "        ");
        }
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendDelegatePropertyHelpers(StringBuilder builder, ExportedTypeModel model, ExportedPropertyModel property)
    {
        var delegateType = property.PropertyType.FullyQualifiedTypeName;
        var signature = property.PropertyType.DelegateSignature ?? throw new InvalidOperationException("Delegate properties require a delegate signature.");

        builder.Append("    private static __JSExportCommon.DelegateBinding<");
        builder.Append(delegateType);
        builder.Append("> ");
        builder.Append(property.DelegateBindingHelperName);
        builder.AppendLine("(global::BunSharp.BunContext context, global::BunSharp.BunValue value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (context.IsNull(value) || context.IsUndefined(value))");
        builder.AppendLine("        {");
        builder.Append("            return new __JSExportCommon.DelegateBinding<");
        builder.Append(delegateType);
        builder.AppendLine(">(null, default, null, false);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var functionRef = __JSExportCommon.ReadJSFunctionRef(context, value)!;");
        builder.Append(delegateType);
        builder.Append(" managed = (");
        builder.Append(string.Join(", ", signature.Parameters.Select(static parameter => $"{parameter.Type.FullyQualifiedTypeName} {parameter.LocalName}")));
        builder.AppendLine(") =>");
        builder.AppendLine("        {");
        builder.AppendLine("            var delegateContext = functionRef.Context;");
        if (signature.Parameters.Length == 0)
        {
            if (signature.ReturnType.Kind == ExportValueKind.Void)
            {
                builder.AppendLine("            _ = functionRef.Call(global::BunSharp.BunValue.Undefined, global::System.ReadOnlySpan<global::BunSharp.BunValue>.Empty);");
                builder.AppendLine("            return;");
            }
            else
            {
                builder.AppendLine("            var result = functionRef.Call(global::BunSharp.BunValue.Undefined, global::System.ReadOnlySpan<global::BunSharp.BunValue>.Empty);");
                builder.Append("            return ");
                builder.Append(ConvertFromBunValue(signature.ReturnType, "result", "delegateContext"));
                builder.AppendLine(";");
            }
        }
        else
        {
            builder.Append("            global::System.Span<global::BunSharp.BunValue> invokeArgs = stackalloc global::BunSharp.BunValue[");
            builder.Append(signature.Parameters.Length.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("];");
            for (var index = 0; index < signature.Parameters.Length; index++)
            {
                var parameter = signature.Parameters[index];
                builder.Append("            invokeArgs[");
                builder.Append(index.ToString(CultureInfo.InvariantCulture));
                builder.Append("] = ");
                builder.Append(ConvertToBunValue(parameter.Type, parameter.LocalName, "delegateContext"));
                builder.AppendLine(";");
            }

            if (signature.ReturnType.Kind == ExportValueKind.Void)
            {
                builder.AppendLine("            _ = functionRef.Call(global::BunSharp.BunValue.Undefined, invokeArgs);");
                builder.AppendLine("            return;");
            }
            else
            {
                builder.AppendLine("            var result = functionRef.Call(global::BunSharp.BunValue.Undefined, invokeArgs);");
                builder.Append("            return ");
                builder.Append(ConvertFromBunValue(signature.ReturnType, "result", "delegateContext"));
                builder.AppendLine(";");
            }
        }
        builder.AppendLine("        };");
        builder.Append("        return new __JSExportCommon.DelegateBinding<");
        builder.Append(delegateType);
        builder.AppendLine(">(managed, value, functionRef, true);");
        builder.AppendLine("    }");
        builder.AppendLine();

        builder.Append("    private static global::BunSharp.BunValue ");
        builder.Append(property.DelegateFunctionHelperName);
        builder.Append('(');
        builder.Append("global::BunSharp.BunContext context, ");
        builder.Append(delegateType);
        builder.AppendLine(" source)");
        builder.AppendLine("    {");
        builder.Append("        return context.CreateFunction(");
        builder.Append(CSharpLiteral(property.ExportName));
        builder.AppendLine(", (callbackContext, args, _) =>");
        builder.AppendLine("        {");
        builder.Append("            __JSExportCommon.EnsureArgumentCount(");
        builder.Append(CSharpLiteral($"{model.ExportName}.{property.ExportName}"));
        builder.Append(", args, ");
        builder.Append(signature.Parameters.Length.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(");");
        AppendParameterReads(builder, signature.Parameters, "            ");
        AppendInvocation(builder, signature.ReturnType, $"source({string.Join(", ", signature.Parameters.Select(static parameter => parameter.LocalName))})", "            ");
        builder.Append("        }, argCount: ");
        builder.Append(signature.Parameters.Length.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(");");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendDelegateMethodHelpers(StringBuilder builder, ExportedTypeModel model, ExportedMethodModel method)
    {
        var delegateType = method.ReturnType.FullyQualifiedTypeName;
        var signature = method.ReturnType.DelegateSignature ?? throw new InvalidOperationException("Delegate method returns require a delegate signature.");

        builder.Append("    private static global::BunSharp.BunValue ");
        builder.Append(method.DelegateFunctionHelperName);
        builder.Append('(');
        builder.Append("global::BunSharp.BunContext context, ");
        builder.Append(delegateType);
        builder.AppendLine(" source)");
        builder.AppendLine("    {");
        builder.Append("        return context.CreateFunction(");
        builder.Append(CSharpLiteral(method.ExportName));
        builder.AppendLine(", (callbackContext, args, _) =>");
        builder.AppendLine("        {");
        builder.Append("            __JSExportCommon.EnsureArgumentCount(");
        builder.Append(CSharpLiteral($"{model.ExportName}.{method.ExportName}"));
        builder.Append(", args, ");
        builder.Append(signature.Parameters.Length.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(");");
        AppendParameterReads(builder, signature.Parameters, "            ");
        AppendInvocation(builder, signature.ReturnType, $"source({string.Join(", ", signature.Parameters.Select(static parameter => parameter.LocalName))})", "            ");
        builder.Append("        }, argCount: ");
        builder.Append(signature.Parameters.Length.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(");");
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
            ExportValueKind.JSObjectRef => $"__JSExportCommon.ReadJSObjectRef({contextExpression}, {valueExpression})",
            ExportValueKind.JSFunctionRef => $"__JSExportCommon.ReadJSFunctionRef({contextExpression}, {valueExpression})",
            ExportValueKind.JSArrayRef => $"__JSExportCommon.ReadJSArrayRef({contextExpression}, {valueExpression})",
            ExportValueKind.JSArrayBufferRef => $"__JSExportCommon.ReadJSArrayBufferRef({contextExpression}, {valueExpression})",
            ExportValueKind.JSTypedArrayRef => $"__JSExportCommon.ReadJSTypedArrayRef({contextExpression}, {valueExpression})",
            ExportValueKind.JSBufferRef => $"__JSExportCommon.ReadJSBufferRef({contextExpression}, {valueExpression})",
            ExportValueKind.ExportedObject => $"{type.HelperId}.UnwrapManaged({contextExpression}, {valueExpression})",
            ExportValueKind.Array => $"__JSExportCommon.{GetArrayReadHelperName(type)}({contextExpression}, {valueExpression})",
            _ => throw new InvalidOperationException($"Unsupported conversion kind {type.Kind}.")
        };

        if (type.RequiresNullCheckedReferenceHandling)
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
            ExportValueKind.JSObjectRef => $"__JSExportCommon.ReferenceValueOrNull({valueExpression})",
            ExportValueKind.JSFunctionRef => $"__JSExportCommon.ReferenceValueOrNull({valueExpression})",
            ExportValueKind.JSArrayRef => $"__JSExportCommon.ReferenceValueOrNull({valueExpression})",
            ExportValueKind.JSArrayBufferRef => $"__JSExportCommon.ReferenceValueOrNull({valueExpression})",
            ExportValueKind.JSTypedArrayRef => $"__JSExportCommon.ReferenceValueOrNull({valueExpression})",
            ExportValueKind.JSBufferRef => $"__JSExportCommon.ReferenceValueOrNull({valueExpression})",
            ExportValueKind.ExportedObject => $"{type.HelperId}.WrapManaged({contextExpression}, {valueExpression})",
            ExportValueKind.Array => $"__JSExportCommon.{GetArrayWriteHelperName(type)}({contextExpression}, {valueExpression})",
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

    private static bool IsRuntimeAffineStaticJsReference(TypeRefModel type)
    {
        return type.Kind is ExportValueKind.JSObjectRef
            or ExportValueKind.JSFunctionRef
            or ExportValueKind.JSArrayRef
            or ExportValueKind.JSArrayBufferRef
            or ExportValueKind.JSTypedArrayRef
            or ExportValueKind.JSBufferRef;
    }

    private readonly struct ExportRule
    {
        public ExportRule(bool enabled, string? name, bool stable, bool hasStableOption)
        {
            Enabled = enabled;
            Name = name;
            Stable = stable;
            HasStableOption = hasStableOption;
        }

        public bool Enabled { get; }

        public string? Name { get; }

        public bool Stable { get; }

        public bool HasStableOption { get; }
    }

    private readonly struct KnownTypeSymbols
    {
        public KnownTypeSymbols(INamedTypeSymbol bunValueSymbol, INamedTypeSymbol jsObjectRefSymbol, INamedTypeSymbol jsFunctionRefSymbol, INamedTypeSymbol jsArrayRefSymbol, INamedTypeSymbol jsArrayBufferRefSymbol, INamedTypeSymbol jsTypedArrayRefSymbol, INamedTypeSymbol jsBufferRefSymbol)
        {
            BunValueSymbol = bunValueSymbol;
            JSObjectRefSymbol = jsObjectRefSymbol;
            JSFunctionRefSymbol = jsFunctionRefSymbol;
            JSArrayRefSymbol = jsArrayRefSymbol;
            JSArrayBufferRefSymbol = jsArrayBufferRefSymbol;
            JSTypedArrayRefSymbol = jsTypedArrayRefSymbol;
            JSBufferRefSymbol = jsBufferRefSymbol;
        }

        public INamedTypeSymbol BunValueSymbol { get; }

        public INamedTypeSymbol JSObjectRefSymbol { get; }

        public INamedTypeSymbol JSFunctionRefSymbol { get; }

        public INamedTypeSymbol JSArrayRefSymbol { get; }

        public INamedTypeSymbol JSArrayBufferRefSymbol { get; }

        public INamedTypeSymbol JSTypedArrayRefSymbol { get; }

        public INamedTypeSymbol JSBufferRefSymbol { get; }
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
            TypeRefModel returnType,
            bool stable)
        {
            MemberName = memberName;
            ExportName = exportName;
            IsStatic = isStatic;
            Parameters = parameters;
            ReturnType = returnType;
            Stable = stable;
        }

        public string MemberName { get; }

        public string ExportName { get; }

        public bool IsStatic { get; }

        public ImmutableArray<ParameterModel> Parameters { get; }

        public TypeRefModel ReturnType { get; }

        public bool Stable { get; }

        public string WrapperName => IsStatic ? $"StaticMethod_{MemberName}" : $"InstanceMethod_{MemberName}";

        public string DelegateFunctionHelperName => $"CreateDelegateFunction_{MemberName}";
    }

    private readonly struct ExportedPropertyModel
    {
        public ExportedPropertyModel(
            string memberName,
            string exportName,
            bool isStatic,
            bool hasGetter,
            bool hasSetter,
            TypeRefModel propertyType,
            bool stable)
        {
            MemberName = memberName;
            ExportName = exportName;
            IsStatic = isStatic;
            HasGetter = hasGetter;
            HasSetter = hasSetter;
            PropertyType = propertyType;
            Stable = stable;
        }

        public string MemberName { get; }

        public string ExportName { get; }

        public bool IsStatic { get; }

        public bool HasGetter { get; }

        public bool HasSetter { get; }

        public TypeRefModel PropertyType { get; }

        public bool Stable { get; }

        public string GetterName => IsStatic ? $"StaticGetter_{MemberName}" : $"InstanceGetter_{MemberName}";

        public string SetterName => IsStatic ? $"StaticSetter_{MemberName}" : $"InstanceSetter_{MemberName}";

        public string DelegateBindingHelperName => $"ReadDelegateBinding_{MemberName}";

        public string DelegateFunctionHelperName => $"CreateDelegateFunction_{MemberName}";
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

    private sealed class TypeRefModel
    {
        public TypeRefModel(ExportValueKind kind, string fullyQualifiedTypeName, string? helperId, bool isNullableReferenceType, TypeRefModel? elementType = null, DelegateSignatureModel? delegateSignature = null)
        {
            Kind = kind;
            Semantic = GetSemantic(kind);
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            HelperId = helperId;
            IsNullableReferenceType = isNullableReferenceType;
            ElementType = elementType;
            DelegateSignature = delegateSignature;
        }

        public ExportValueKind Kind { get; }

        public ExportValueSemantic Semantic { get; }

        public string FullyQualifiedTypeName { get; }

        public string? HelperId { get; }

        public bool IsNullableReferenceType { get; }

        public TypeRefModel? ElementType { get; }

        public DelegateSignatureModel? DelegateSignature { get; }

        public bool RequiresNullCheckedReferenceHandling => Semantic is ExportValueSemantic.Snapshot or ExportValueSemantic.ManagedReference or ExportValueSemantic.JsReference or ExportValueSemantic.StableCollection or ExportValueSemantic.SharedBinary or ExportValueSemantic.DelegateReference;

        public bool ShouldDisposePreviousValueOnReplacement => Semantic is ExportValueSemantic.JsReference or ExportValueSemantic.StableCollection or ExportValueSemantic.SharedBinary;

        public bool CanUseStableOption => Kind is ExportValueKind.ByteArray or ExportValueKind.Array;

        public bool IsDelegate => Kind == ExportValueKind.Delegate;
    }

    private readonly struct DelegateSignatureModel
    {
        public DelegateSignatureModel(ImmutableArray<ParameterModel> parameters, TypeRefModel returnType)
        {
            Parameters = parameters;
            ReturnType = returnType;
        }

        public ImmutableArray<ParameterModel> Parameters { get; }

        public TypeRefModel ReturnType { get; }
    }

    private static ExportValueSemantic GetSemantic(ExportValueKind kind)
    {
        return kind switch
        {
            ExportValueKind.Bool or ExportValueKind.Int32 or ExportValueKind.Double => ExportValueSemantic.Value,
            ExportValueKind.String or ExportValueKind.ByteArray or ExportValueKind.Array => ExportValueSemantic.Snapshot,
            ExportValueKind.BunValue => ExportValueSemantic.TemporaryJsValue,
            ExportValueKind.JSObjectRef or ExportValueKind.JSFunctionRef => ExportValueSemantic.JsReference,
            ExportValueKind.JSArrayRef => ExportValueSemantic.StableCollection,
            ExportValueKind.JSArrayBufferRef or ExportValueKind.JSTypedArrayRef or ExportValueKind.JSBufferRef => ExportValueSemantic.SharedBinary,
            ExportValueKind.Delegate => ExportValueSemantic.DelegateReference,
            ExportValueKind.ExportedObject => ExportValueSemantic.ManagedReference,
            ExportValueKind.Void => ExportValueSemantic.Void,
            _ => throw new InvalidOperationException($"Unsupported export value kind {kind}.")
        };
    }

    private enum ExportValueKind
    {
        Bool,
        Int32,
        Double,
        String,
        ByteArray,
        BunValue,
        JSObjectRef,
        JSFunctionRef,
        JSArrayRef,
        JSArrayBufferRef,
        JSTypedArrayRef,
        JSBufferRef,
        Delegate,
        ExportedObject,
        Array,
        Void,
    }

    private enum ExportValueSemantic
    {
        Value,
        Snapshot,
        TemporaryJsValue,
        ManagedReference,
        JsReference,
        StableCollection,
        SharedBinary,
        DelegateReference,
        Void,
    }
}