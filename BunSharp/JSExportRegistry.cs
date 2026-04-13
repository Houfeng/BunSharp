using System.Collections.Concurrent;

namespace BunSharp;

public static class JSExportRegistry
{
    // ConcurrentDictionary is used here purely for simplicity: it eliminates the
    // need for a manual SyncRoot + lock pair. There is no hot-path concern —
    // RegisterExport and ExportType are both called only once during initialization,
    // not during JS execution. [ThreadStatic] is NOT applicable: this is a global
    // registry whose entries must be visible across all threads.
    private static readonly ConcurrentDictionary<Type, Func<BunContext, bool>> JsExports = new();
    private static readonly ConcurrentDictionary<Type, Func<BunContext, BunValue, object?>> JsExportUnwrappers = new();
    private static readonly ConcurrentDictionary<Type, Func<BunContext, object, BunValue?>> JsExportValueLookups = new();

    public static void RegisterExport(
        Type exportType,
        Func<BunContext, bool> register,
        Func<BunContext, BunValue, object?>? unwrap = null,
        Func<BunContext, object, BunValue?>? tryGetValue = null)
    {
        ArgumentNullException.ThrowIfNull(exportType);
        ArgumentNullException.ThrowIfNull(register);
        JsExports[exportType] = register;

        if (unwrap is not null)
        {
            JsExportUnwrappers[exportType] = unwrap;
        }
        else
        {
            JsExportUnwrappers.TryRemove(exportType, out _);
        }

        if (tryGetValue is not null)
        {
            JsExportValueLookups[exportType] = tryGetValue;
        }
        else
        {
            JsExportValueLookups.TryRemove(exportType, out _);
        }
    }

    private static bool TryExportType(BunContext context, Type type)
    {
        return JsExports.TryGetValue(type, out var handler) && handler(context);
    }

    internal static bool TryUnwrapExported<T>(BunContext context, BunValue value, out T? result) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);

        if (JsExportUnwrappers.TryGetValue(typeof(T), out var handler) && handler(context, value) is T typed)
        {
            result = typed;
            return true;
        }

        result = null;
        return false;
    }

    internal static bool TryGetExportedValue<T>(BunContext context, T instance, out BunValue value) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(instance);

        if (JsExportValueLookups.TryGetValue(typeof(T), out var handler))
        {
            var existing = handler(context, instance);
            if (existing.HasValue)
            {
                value = existing.Value;
                return true;
            }
        }

        value = default;
        return false;
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