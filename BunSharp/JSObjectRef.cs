using System;

namespace BunSharp;

/// <summary>
/// Retains a live JavaScript object or function across calls.
/// Call <see cref="Dispose"/> explicitly when the reference is no longer
/// needed. If the wrapper later becomes unreachable and is garbage-collected,
/// a finalizer queues the native release back onto the runtime-owning thread so
/// the JS object can become collectible too. Runtime teardown remains the
/// last-resort fallback and explicit disposal is still the recommended
/// ownership model.
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

    ~JSObjectRef()
    {
        _state?.DisposeFromFinalizer();
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
        private readonly BunRuntime _runtime;
        private readonly Action _releaseProtectedValueAction;
        private BunContext? _context;
        private IDisposable? _cleanupRegistration;
        private BunValue _value;
        private bool _disposed;
        private bool _releaseQueued;

        public ReleaseState(BunContext context, BunValue value)
        {
            _runtime = context.GetOwningRuntime();
            _releaseProtectedValueAction = ReleaseProtectedValue;
            _context = context;
            _value = value;

            var protectedValue = false;
            try
            {
                context.Protect(value);
                protectedValue = true;
                _cleanupRegistration = context.RegisterPreDestroyCleanup(_releaseProtectedValueAction);
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

        public void DisposeFromFinalizer()
        {
            try
            {
                lock (_disposeSync)
                {
                    if (_disposed || _releaseQueued)
                    {
                        return;
                    }

                    if (!_runtime.TryPost(_releaseProtectedValueAction))
                    {
                        return;
                    }

                    _releaseQueued = true;
                }
            }
            catch
            {
                // Finalizers must never let exceptions escape the GC finalizer thread.
                // Runtime teardown still keeps the pre-destroy cleanup fallback alive.
            }
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
                _releaseQueued = false;
                _disposed = true;
            }
        }
    }
}