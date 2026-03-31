using System.Reflection;

namespace BunSharp;

public static class JSExportContextExtensions
{
    public static void ExportType<T>(this BunContext context)
    {
        ExportType(context, typeof(T));
    }

    public static void ExportType(this BunContext context, Type type)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(type);

        if (!BunRegistry.TryGetJsExports(type.Assembly, out _, out var registerType) || registerType is null)
        {
            throw new InvalidOperationException($"No generated BunSharp exports were registered for assembly '{type.Assembly.GetName().Name}'.");
        }

        if (!registerType(context, type))
        {
            throw new InvalidOperationException($"Type '{type.FullName}' is not registered for JS export in assembly '{type.Assembly.GetName().Name}'.");
        }
    }

    public static void ExportAll(this BunContext context, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(assembly);

        if (!BunRegistry.TryGetJsExports(assembly, out var registerAll, out _) || registerAll is null)
        {
            throw new InvalidOperationException($"No generated BunSharp exports were registered for assembly '{assembly.GetName().Name}'.");
        }

        registerAll(context);
    }
}