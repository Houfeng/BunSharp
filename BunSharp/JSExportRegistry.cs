using System.Collections.Concurrent;

namespace BunSharp;

public static class JSExportRegistry
{
    // ConcurrentDictionary gives lock-free reads, which matters because
    // TryExportType is called on the Bun runtime thread while RegisterExport
    // is called on the registering thread (typically at startup).
    // [ThreadStatic] is NOT applicable here: this is a global registry whose
    // entries must be visible across all threads.
    private static readonly ConcurrentDictionary<Type, Func<BunContext, bool>> JsExports = new();

    public static void RegisterExport(
        Type exportType,
        Func<BunContext, bool> register)
    {
        ArgumentNullException.ThrowIfNull(exportType);
        ArgumentNullException.ThrowIfNull(register);
        JsExports[exportType] = register;
    }

    private static bool TryExportType(BunContext context, Type type)
    {
        return JsExports.TryGetValue(type, out var handler) && handler(context);
    }

	public static void ExportType(this BunContext context, Type type)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(type);
		if (!TryExportType(context, type))
		{
			throw new InvalidOperationException($"Type '{type.FullName}' is not registered for JS export.");
		}
	}

	public static void ExportType<T>(this BunContext context)
	{
		ExportType(context, typeof(T));
	}
}