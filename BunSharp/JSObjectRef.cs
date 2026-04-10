namespace BunSharp;

public sealed class JSObjectRef : IDisposable
{
    private BunContext? _context;
    private BunValue _value;
    private int _disposed;

    public JSObjectRef(BunContext context, BunValue value)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.CanRetainManagedResources)
        {
            throw new InvalidOperationException("Persistent JS references require a runtime-owned BunContext.");
        }

        if (context.IsNull(value) || context.IsUndefined(value))
        {
            throw new ArgumentException("Persistent JS references cannot wrap null or undefined.", nameof(value));
        }

        if (!context.IsObject(value) && !context.IsCallable(value))
        {
            throw new ArgumentException("Persistent JS references require a JS object or function value.", nameof(value));
        }

        _context = context;
        _value = value;

        context.Protect(value);
        context.RegisterPreDestroyCleanup(ReleaseProtectedValue);
    }

    public BunContext Context
    {
        get
        {
            ObjectDisposedException.ThrowIf(_context is null, this);
            return _context;
        }
    }

    public BunValue Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_context is null, this);
            return _value;
        }
    }

    public BunValue GetProperty(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Context.GetProperty(Value, key);
    }

    public bool SetProperty(string key, BunValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Context.SetProperty(Value, key, value);
    }

    public BunValue GetIndex(uint index)
    {
        return Context.GetIndex(Value, index);
    }

    public bool SetIndex(uint index, BunValue value)
    {
        return Context.SetIndex(Value, index, value);
    }

    public void Dispose()
    {
        ReleaseProtectedValue();
        GC.SuppressFinalize(this);
    }

    private void ReleaseProtectedValue()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var context = Interlocked.Exchange(ref _context, null);
        var value = _value;
        _value = BunValue.Undefined;

        if (context is not null)
        {
            context.Unprotect(value);
        }
    }
}