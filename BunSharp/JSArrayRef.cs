using System;

namespace BunSharp;

/// <summary>
/// Retains a live JavaScript Array with stable identity.
/// Call <see cref="Dispose"/> explicitly when the array reference is no longer
/// needed. If the wrapper graph is abandoned, the wrapped
/// <see cref="JSObjectRef"/> provides the same finalizer-backed fallback
/// release path; explicit disposal is still recommended.
/// </summary>
public sealed class JSArrayRef : IDisposable
{
    private readonly JSObjectRef _objectRef;

    public JSArrayRef(BunContext context, BunValue value)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.IsArray(value))
        {
            throw new ArgumentException("Stable array references require a JS Array value.", nameof(value));
        }

        _objectRef = new JSObjectRef(context, value);
    }

    public BunContext Context => _objectRef.Context;

    public BunValue Value => _objectRef.Value;

    public long Length => Context.GetArrayLength(Value);

    public BunValue GetIndex(uint index)
    {
        return Context.GetIndex(Value, index);
    }

    public bool SetIndex(uint index, BunValue value)
    {
        return Context.SetIndex(Value, index, value);
    }

    public bool Append(BunValue value)
    {
        return SetIndex(checked((uint)Length), value);
    }

    public void Dispose()
    {
        _objectRef.Dispose();
        GC.SuppressFinalize(this);
    }
}