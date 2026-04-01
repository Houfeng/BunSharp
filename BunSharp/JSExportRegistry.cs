namespace BunSharp;

public static class JSExportRegistry
{
	private static readonly object SyncRoot = new();
	private static readonly Dictionary<Type, Func<BunContext, bool>> JsExports = new();

	public static void RegisterExport(
		Type exportType,
		Func<BunContext, bool> register)
	{
		ArgumentNullException.ThrowIfNull(exportType);
		ArgumentNullException.ThrowIfNull(register);
		lock (SyncRoot)
		{
			JsExports[exportType] = register;
		}
	}

	private static bool TryExportType(BunContext context, Type type)
	{
		Func<BunContext, bool>? handler;
		lock (SyncRoot)
		{
			JsExports.TryGetValue(type, out handler);
		}
		return handler is not null && handler(context);
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