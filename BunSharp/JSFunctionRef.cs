namespace BunSharp;

public sealed class JSFunctionRef : IDisposable
{
    private readonly JSObjectRef _objectRef;

    public JSFunctionRef(BunContext context, BunValue value)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.IsCallable(value))
        {
            throw new ArgumentException("Persistent JS function references require a callable value.", nameof(value));
        }

        _objectRef = new JSObjectRef(context, value);
    }

    public BunContext Context => _objectRef.Context;

    public BunValue Value => _objectRef.Value;

    public BunValue Call(BunValue thisValue, ReadOnlySpan<BunValue> args)
    {
        return Context.Call(Value, thisValue, args);
    }

    public int CallAsync(BunValue thisValue, ReadOnlySpan<BunValue> args)
    {
        return Context.CallAsync(Value, thisValue, args);
    }

    public bool TryCallAsync(BunValue thisValue, ReadOnlySpan<BunValue> args)
    {
        return Context.TryCallAsync(Value, thisValue, args);
    }

    public void Dispose()
    {
        _objectRef.Dispose();
        GC.SuppressFinalize(this);
    }
}