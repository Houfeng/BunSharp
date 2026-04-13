namespace BunSharp;

/// <summary>
/// Retains a live JavaScript object or function across calls.
/// Call <see cref="Dispose"/> explicitly when the reference is no longer
/// needed. Runtime teardown is only a fallback release path and should not be
/// treated as the normal ownership model.
/// </summary>
public sealed class JSObjectRef : IDisposable
{
    private readonly ReleaseState _state;

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

        _state = new ReleaseState(context, value);
    }

    public BunContext Context
    {
        get
        {
            return _state.Context;
        }
    }

    public BunValue Value
    {
        get
        {
            return _state.Value;
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
        _state.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class ReleaseState : IDisposable
    {
        private readonly object _disposeSync = new();
        private BunContext? _context;
        private IDisposable? _cleanupRegistration;
        private BunValue _value;
        private bool _disposed;

        public ReleaseState(BunContext context, BunValue value)
        {
            _context = context;
            _value = value;

            var protectedValue = false;
            try
            {
                context.Protect(value);
                protectedValue = true;
                _cleanupRegistration = context.RegisterPreDestroyCleanup(ReleaseProtectedValue);
            }
            catch (Exception ex)
            {
                _cleanupRegistration = null;
                _value = BunValue.Undefined;
                _context = null;

                if (protectedValue)
                {
                    try
                    {
                        context.Unprotect(value);
                    }
                    catch (Exception rollbackEx)
                    {
                        throw new AggregateException("Failed to initialize JSObjectRef and rollback the protected JS reference.", ex, rollbackEx);
                    }
                }

                throw;
            }
        }

        public BunContext Context
        {
            get
            {
                var context = _context;
                if (context is null)
                {
                    throw new ObjectDisposedException(nameof(JSObjectRef));
                }

                return context;
            }
        }

        public BunValue Value
        {
            get
            {
                if (_context is null)
                {
                    throw new ObjectDisposedException(nameof(JSObjectRef));
                }

                return _value;
            }
        }

        public void Dispose()
        {
            ReleaseProtectedValue();
        }

        private void ReleaseProtectedValue()
        {
            lock (_disposeSync)
            {
                if (_disposed)
                {
                    return;
                }

                var context = _context;
                if (context is null)
                {
                    _disposed = true;
                    return;
                }

                var value = _value;
                context.Unprotect(value);
                _cleanupRegistration?.Dispose();
                _cleanupRegistration = null;
                _value = BunValue.Undefined;
                _context = null;
                _disposed = true;
            }
        }
    }
}