using System.Runtime.InteropServices;
using BunSharp.Interop;

namespace BunSharp;

public sealed class BunRuntime : IDisposable
{
    private readonly HashSet<IDisposable> _ownedResources = [];
    private readonly Dictionary<BunValue, BunObjectFinalizerRegistration> _objectFinalizerRegistrations = [];
    private readonly List<Action> _cleanupCallbacks = [];
    private readonly int _threadId;
    private BunContext? _context;
    private bool _disposed;

    private BunRuntime(nint handle)
    {
        Handle = handle;
        _threadId = Environment.CurrentManagedThreadId;
    }

    public nint Handle { get; private set; }

    public BunContext Context
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VerifyThread();
            return _context ??= new BunContext(this, BunNative.Context(Handle));
        }
    }

    public int EventFileDescriptor
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VerifyThread();
            return BunNative.GetEventFd(Handle);
        }
    }

    public static BunRuntime Create(string? cwd = null)
    {
        return Create(new BunRuntimeOptions
        {
            Cwd = cwd,
        });
    }

    public static BunRuntime Create(BunRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        BunNativeLibraryResolver.EnsureLoaded();
        Utf8NativeString? cwd = null;
        Utf8NativeString? debuggerListenUrl = null;

        if (!string.IsNullOrEmpty(options.Cwd))
        {
            cwd = Utf8NativeString.Allocate(options.Cwd);
        }

        if (!string.IsNullOrEmpty(options.DebuggerListenUrl))
        {
            debuggerListenUrl = Utf8NativeString.Allocate(options.DebuggerListenUrl);
        }

        try
        {
            unsafe
            {
                var initializeOptions = new BunInitializeOptions
                {
                    Cwd = cwd?.Pointer ?? 0,
                    DebuggerMode = MapDebuggerMode(options.DebuggerMode),
                    DebuggerListenUrl = debuggerListenUrl?.Pointer ?? 0,
                };

                var handle = BunNative.Initialize(&initializeOptions);
                if (handle == 0)
                {
                    throw new BunException("bun_initialize returned a null runtime.");
                }

                return new BunRuntime(handle);
            }
        }
        finally
        {
            debuggerListenUrl?.Dispose();
            cwd?.Dispose();
        }
    }

    public bool RunPendingJobs()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyThread();
        return BunNative.RunPendingJobs(Handle) != 0;
    }

    public void Wakeup()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BunNative.Wakeup(Handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (Handle != 0)
        {
            BunNative.Destroy(Handle);
            Handle = 0;
        }

        foreach (var callback in _cleanupCallbacks)
        {
            callback();
        }

        _cleanupCallbacks.Clear();

        if (_objectFinalizerRegistrations.Count > 0)
        {
            var registrations = _objectFinalizerRegistrations.Values.ToArray();
            _objectFinalizerRegistrations.Clear();
            for (var i = registrations.Length - 1; i >= 0; i--)
                registrations[i].Dispose();
        }

        var snapshot = _ownedResources.Count > 0 ? _ownedResources.ToArray() : null;
        _ownedResources.Clear();
        if (snapshot is not null)
        {
            for (var i = snapshot.Length - 1; i >= 0; i--)
                snapshot[i].Dispose();
        }
        if (_context is not null)
        {
            BunManagedCallbackRegistry.removeContextCache(_context.Handle);
        }
        _disposed = true;
    }

    internal void Retain(IDisposable resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ownedResources.Add(resource);
    }

    internal void RetainWithAutoRelease(BunCallbackHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ownedResources.Add(handle);
        handle.SetRemoveFromOwner(() => _ownedResources.Remove(handle));
    }

    internal BunObjectFinalizerRegistration? GetOrCreateObjectFinalizerRegistration(BunContext context, BunValue target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_objectFinalizerRegistrations.TryGetValue(target, out var registration))
            return registration;

        registration = BunObjectFinalizerRegistration.Create(this, context, target);
        if (registration is null)
            return null;

        _objectFinalizerRegistrations[target] = registration;
        return registration;
    }

    internal void RemoveObjectFinalizerRegistration(BunValue target, BunObjectFinalizerRegistration registration)
    {
        if (_objectFinalizerRegistrations.TryGetValue(target, out var current) && ReferenceEquals(current, registration))
            _objectFinalizerRegistrations.Remove(target);
    }

    internal void RegisterCleanup(Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cleanupCallbacks.Add(callback);
    }

    internal void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException("BunRuntime APIs must be called from the thread that initialized the runtime.");
        }
    }

    private static BunNativeDebuggerMode MapDebuggerMode(BunDebuggerMode debuggerMode)
    {
        return debuggerMode switch
        {
            BunDebuggerMode.Off => BunNativeDebuggerMode.Off,
            BunDebuggerMode.Attach => BunNativeDebuggerMode.Attach,
            BunDebuggerMode.Wait => BunNativeDebuggerMode.Wait,
            BunDebuggerMode.Break => BunNativeDebuggerMode.Break,
            _ => throw new ArgumentOutOfRangeException(nameof(debuggerMode), debuggerMode, "Unsupported debugger mode."),
        };
    }
}

internal sealed class BunObjectFinalizerRegistration : IDisposable
{
    private readonly BunRuntime _owner;
    private readonly BunValue _target;
    private readonly List<BunCallbackHandle> _callbackHandles = [];
    private BunManagedFinalizer? _managedFinalizer;
    private nint _managedFinalizerUserData;
    private bool _hasManagedFinalizer;
    private bool _disposed;

    private BunObjectFinalizerRegistration(BunRuntime owner, BunValue target)
    {
        _owner = owner;
        _target = target;
    }

    internal static BunObjectFinalizerRegistration? Create(BunRuntime owner, BunContext context, BunValue target)
    {
        var registration = new BunObjectFinalizerRegistration(owner, target);
        var handle = BunManagedCallbackRegistry.CreateFinalizer(_ => registration.OnTargetFinalized(), 0);
        try
        {
            var disposerHandle = GCHandle.Alloc(handle);
            var disposerPtr = GCHandle.ToIntPtr(disposerHandle);
            handle.SetDisposerHandle(disposerPtr);
            if (BunNative.DefineFinalizer(context.Handle, target, BunManagedCallbackRegistry.FinalizerPointer, disposerPtr) == 0)
            {
                handle.Dispose();
                return null;
            }

            owner.RetainWithAutoRelease(handle);
            return registration;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal void AddCallbackHandle(BunCallbackHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _callbackHandles.Add(handle);
    }

    internal bool AddManagedFinalizer(BunManagedFinalizer callback, nint userdata)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hasManagedFinalizer)
            return false;

        _managedFinalizer = callback;
        _managedFinalizerUserData = userdata;
        _hasManagedFinalizer = true;
        return true;
    }

    internal void OnTargetFinalized()
    {
        DisposeCore(runManagedFinalizers: true);
    }

    public void Dispose()
    {
        DisposeCore(runManagedFinalizers: false);
    }

    private void DisposeCore(bool runManagedFinalizers)
    {
        if (_disposed)
            return;

        _disposed = true;
        _owner.RemoveObjectFinalizerRegistration(_target, this);

        if (runManagedFinalizers && _hasManagedFinalizer)
        {
            try
            {
                _managedFinalizer!(_managedFinalizerUserData);
            }
            catch
            {
            }
        }

        for (var i = _callbackHandles.Count - 1; i >= 0; i--)
            _callbackHandles[i].Dispose();

        _callbackHandles.Clear();
        _managedFinalizer = null;
        _managedFinalizerUserData = 0;
        _hasManagedFinalizer = false;
    }
}