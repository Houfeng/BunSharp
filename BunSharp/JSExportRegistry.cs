using System.Reflection;

namespace BunSharp;

public static class JSExportRegistry
{
	private readonly record struct JsExportRegistration(
		Action<BunContext> RegisterAll,
		Func<BunContext, Type, bool> RegisterType);

	private static readonly object SyncRoot = new();
	private static readonly Dictionary<Assembly, JsExportRegistration> JsExports = new();

	public static void RegisterExports(
		Assembly assembly,
		Action<BunContext> registerAll,
		Func<BunContext, Type, bool> registerType)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentNullException.ThrowIfNull(registerAll);
		ArgumentNullException.ThrowIfNull(registerType);

		lock (SyncRoot)
		{
			JsExports[assembly] = new JsExportRegistration(registerAll, registerType);
		}
	}

	internal static bool TryGetExports(
		Assembly assembly,
		out Action<BunContext>? registerAll,
		out Func<BunContext, Type, bool>? registerType)
	{
		ArgumentNullException.ThrowIfNull(assembly);

		lock (SyncRoot)
		{
			if (JsExports.TryGetValue(assembly, out var registration))
			{
				registerAll = registration.RegisterAll;
				registerType = registration.RegisterType;
				return true;
			}
		}

		registerAll = null;
		registerType = null;
		return false;
	}
}