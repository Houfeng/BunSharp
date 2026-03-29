using LibbunSharp.Interop;

namespace LibbunSharp;

public sealed class BunRuntime : IDisposable
{
    private readonly List<IDisposable> _ownedResources = [];
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
        BunNativeLibraryResolver.EnsureLoaded();
        var handle = BunNative.Initialize(cwd);
        if (handle == 0)
        {
            throw new BunException("bun_initialize returned a null runtime.");
        }

        return new BunRuntime(handle);
    }

    public BunEvaluationResult Evaluate(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyThread();

        var result = BunNative.EvalString(Handle, code);
        return new BunEvaluationResult(result.Success != 0, result.ErrorMessage);
    }

    public BunEvaluationResult EvaluateFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyThread();

        var result = BunNative.EvalFile(Handle, path);
        return new BunEvaluationResult(result.Success != 0, result.ErrorMessage);
    }

    public void EvaluateOrThrow(string code)
    {
        Evaluate(code).EnsureSuccess();
    }

    public void EvaluateFileOrThrow(string path)
    {
        EvaluateFile(path).EnsureSuccess();
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

    internal void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException("BunRuntime APIs must be called from the thread that initialized the runtime.");
        }
    }
}