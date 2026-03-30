using System.Reflection;
using System.Runtime.ExceptionServices;

namespace LibbunSharp;

public static class JSExportContextExtensions
{
    private const string GeneratedExportsTypeName = "LibbunSharp.Generated.BunGeneratedExports";

    public static void ExportType<T>(this BunContext context)
    {
        ExportType(context, typeof(T));
    }

    public static void ExportType(this BunContext context, Type type)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(type);

        var exportsType = type.Assembly.GetType(GeneratedExportsTypeName, throwOnError: false);
        if (exportsType is null)
        {
            throw new InvalidOperationException($"No generated LibbunSharp exports were found in assembly '{type.Assembly.GetName().Name}'.");
        }

        var registerTypeMethod = exportsType.GetMethod(
            "RegisterType",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [typeof(BunContext), typeof(Type)],
            modifiers: null);

        if (registerTypeMethod is null)
        {
            throw new InvalidOperationException($"Type '{GeneratedExportsTypeName}' is missing RegisterType(BunContext, Type).");
        }

        bool registered;
        try
        {
            registered = (bool?)registerTypeMethod.Invoke(null, [context, type]) ?? false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        if (!registered)
        {
            throw new InvalidOperationException($"Type '{type.FullName}' is not registered for JS export in assembly '{type.Assembly.GetName().Name}'.");
        }
    }

    public static void ExportAll(this BunContext context, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(assembly);

        var exportsType = assembly.GetType(GeneratedExportsTypeName, throwOnError: false);
        if (exportsType is null)
        {
            throw new InvalidOperationException($"No generated LibbunSharp exports were found in assembly '{assembly.GetName().Name}'.");
        }

        var registerAllMethod = exportsType.GetMethod(
            "RegisterAll",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [typeof(BunContext)],
            modifiers: null);

        if (registerAllMethod is null)
        {
            throw new InvalidOperationException($"Type '{GeneratedExportsTypeName}' is missing RegisterAll(BunContext).");
        }

        try
        {
            registerAllMethod.Invoke(null, [context]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}