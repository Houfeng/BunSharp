using BunSharp.Interop;

namespace BunSharp;

public sealed class BunRuntime : IDisposable
{
    private readonly List<IDisposable> _ownedResources = [];
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

        for (var index = _ownedResources.Count - 1; index >= 0; index--)
        {
            _ownedResources[index].Dispose();
        }

        _ownedResources.Clear();
        _disposed = true;
    }

    internal void Retain(IDisposable resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ownedResources.Add(resource);
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