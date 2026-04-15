using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using BunSharp.Interop;

namespace BunSharp;

public sealed class BunRuntime : IDisposable
{
    private readonly HashSet<IDisposable> _ownedResources = [];
    private readonly Dictionary<BunValue, BunObjectFinalizerRegistration> _objectFinalizerRegistrations = [];
    private readonly LinkedList<CleanupRegistration> _preDestroyCleanupCallbacks = [];
    private readonly LinkedList<CleanupRegistration> _cleanupCallbacks = [];
    private readonly int _threadId;
    private BunCallbackHandle? _eventCallbackHandle;
    private BunContext? _context;
    private int _isDisposing;
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
            if (_context is not null)
                return _context;

            var contextHandle = BunNative.Context(Handle);
            BunManagedCallbackRegistry.RegisterContextOwner(contextHandle, this);
            _context = new BunContext(this, contextHandle);
            return _context;
        }
    }

    /// <summary>
    /// Gets the underlying OS event loop file descriptor (epoll fd on Linux, kqueue fd on macOS).
    /// The fd becomes readable when I/O is ready or <see cref="Wakeup"/> / async calls are invoked from another thread.
    /// JS timers are NOT signaled through this fd — use <see cref="GetWaitHint"/> as the timeout when polling
    /// so timers fire on time.
    /// Returns -1 on Windows (IOCP has no pollable fd) or if unavailable.
    /// Intended for Mode B (fd merge) integration only.
    /// Prefer <see cref="SetEventCallback(BunManagedEventCallback?, nint)"/> for a cross-platform wake-up mechanism.
    /// </summary>
    public int EventFileDescriptor
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VerifyThread();
            return BunNative.GetEventFd(Handle);
        }
    }

    /// <summary>
    /// Raised synchronously whenever BunSharp reports a runtime diagnostic.
    /// Handlers run on the originating thread, which may be a Bun-managed background thread
    /// or the runtime-owning thread during cleanup/finalization paths.
    /// Handlers should be fast, thread-safe, and must not call runtime-affine APIs from non-owning threads.
    /// Error handler exceptions are not suppressed and propagate through the current reporting path.
    /// </summary>
    public event EventHandler<BunRuntimeErrorEventArgs>? Error;

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

        BunRuntime? runtime = null;
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

                runtime = new BunRuntime(handle);
                return runtime;
            }
        }
        finally
        {
            if (runtime is not null)
            {
                // Initialization succeeded: libbun retains the pointer asynchronously
                // (e.g., deferred WebSocket inspector setup). Transfer ownership to the
                // runtime so the native strings live until the runtime is disposed.
                if (debuggerListenUrl.HasValue)
                {
                    var url = debuggerListenUrl.Value;
                    runtime.RegisterCleanup(() => url.Dispose());
                }
                if (cwd.HasValue)
                {
                    var c = cwd.Value;
                    runtime.RegisterCleanup(() => c.Dispose());
                }
            }
            else
            {
                // Initialization failed: free immediately.
                debuggerListenUrl?.Dispose();
                cwd?.Dispose();
            }
        }
    }

    /// <summary>
    /// Drives the Bun event loop non-blockingly. Call this from the thread that created the runtime
    /// to process pending timers, promises, I/O callbacks, etc.
    /// <para>
    /// Return-value semantics:
    /// <see cref="BunPendingJobsResult.Idle"/> — fully idle, no active handles or pending work.
    /// <see cref="BunPendingJobsResult.Spin"/> — more work is runnable immediately; call again.
    /// <see cref="BunPendingJobsResult.Wait"/> — runtime is active but waiting for I/O or timers; return to your host loop.
    /// </para>
    /// </summary>
    public BunPendingJobsResult RunPendingJobs()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyThread();
        return BunNative.RunPendingJobs(Handle);
    }

    /// <summary>
    /// Returns the recommended wait timeout in milliseconds for the embed host.
    /// <para>
    /// Return values:
    ///   0 — work is runnable right now; call <see cref="RunPendingJobs"/> immediately.
    ///  -1 — no JS timers pending; wait indefinitely on I/O or <see cref="Wakeup"/>.
    ///  &gt;0 — milliseconds until the next JS timer fires.
    /// </para>
    /// Intended for Mode B (fd merge) integration with <see cref="EventFileDescriptor"/>.
    /// </summary>
    public long GetWaitHint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyThread();
        return BunNative.GetWaitHint(Handle);
    }

    /// <summary>
    /// Wakes the runtime event loop from any thread so the next <see cref="RunPendingJobs"/> processes queued work.
    /// Calls queued via <see cref="BunContext.TryCallAsync(BunValue, BunValue, ReadOnlySpan{BunValue})"/> already wake the loop automatically.
    /// </summary>
    public void Wakeup()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BunNative.Wakeup(Handle);
    }

    /// <summary>
    /// Registers a managed callback invoked by libbun from a background thread whenever the runtime has ready work.
    /// Pass <see langword="null"/> to unregister the current callback.
    /// The callback must be thread-safe and should only wake or signal the host loop; drive Bun itself later from the owning thread.
    /// If the callback throws, BunSharp reports the failure through <see cref="Error"/>.
    /// </summary>
    public void SetEventCallback(BunManagedEventCallback? callback, nint userdata = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyThread();

        BunCallbackHandle? nextHandle = null;
        try
        {
            if (callback is not null)
            {
                nextHandle = BunManagedCallbackRegistry.CreateEventCallback(callback, this, userdata);
            }

            BunNative.SetEventCallback(
                Handle,
                callback is null ? 0 : BunManagedCallbackRegistry.EventCallbackPointer,
                nextHandle?.Pointer ?? 0);

            Interlocked.Exchange(ref _eventCallbackHandle, nextHandle)?.Dispose();
            nextHandle = null;
        }
        catch
        {
            nextHandle?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        VerifyThread();
        Interlocked.Exchange(ref _isDisposing, 1);

        List<Exception>? cleanupExceptions = null;

        RunCleanupCallbacks(this, _preDestroyCleanupCallbacks, "pre-destroy", ref cleanupExceptions);

        if (Handle != 0)
        {
            BunNative.SetEventCallback(Handle, 0, 0);
            DisposeCleanupResource(
                this,
                Interlocked.Exchange(ref _eventCallbackHandle, null),
                "event callback handle",
                ref cleanupExceptions);
            BunNative.Destroy(Handle);
            Handle = 0;
        }
        else
        {
            DisposeCleanupResource(
                this,
                Interlocked.Exchange(ref _eventCallbackHandle, null),
                "event callback handle",
                ref cleanupExceptions);
        }

        RunCleanupCallbacks(this, _cleanupCallbacks, "post-destroy", ref cleanupExceptions);

        if (_objectFinalizerRegistrations.Count > 0)
        {
            var registrations = new BunObjectFinalizerRegistration[_objectFinalizerRegistrations.Count];
            _objectFinalizerRegistrations.Values.CopyTo(registrations, 0);
            _objectFinalizerRegistrations.Clear();
            for (var i = registrations.Length - 1; i >= 0; i--)
                DisposeCleanupResource(this, registrations[i], "object finalizer registration", ref cleanupExceptions);
        }

        IDisposable[]? snapshot = null;
        if (_ownedResources.Count > 0)
        {
            snapshot = new IDisposable[_ownedResources.Count];
            _ownedResources.CopyTo(snapshot);
        }
        _ownedResources.Clear();
        if (snapshot is not null)
        {
            for (var i = snapshot.Length - 1; i >= 0; i--)
                DisposeCleanupResource(this, snapshot[i], "owned resource", ref cleanupExceptions);
        }
        if (_context is not null)
        {
            BunManagedCallbackRegistry.removeContextCache(_context.Handle);
        }

        _disposed = true;

        if (cleanupExceptions is not null)
        {
            throw new AggregateException("One or more BunRuntime cleanup operations failed.", cleanupExceptions);
        }
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

    internal IDisposable RegisterCleanup(Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RegisterCleanupCore(CleanupPhase.PostDestroy, callback);
    }

    internal IDisposable RegisterPreDestroyCleanup(Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RegisterCleanupCore(CleanupPhase.PreDestroy, callback);
    }

    internal void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException("BunRuntime APIs must be called from the thread that initialized the runtime.");
        }
    }

    internal void ReportDiagnostic(BunRuntimeDiagnosticSource source, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var handler = Error;
        if (handler is null)
            return;
        var errorArgs = new BunRuntimeErrorEventArgs(
            source,
            exception,
            Environment.CurrentManagedThreadId != _threadId,
            Volatile.Read(ref _isDisposing) != 0,
            Environment.CurrentManagedThreadId,
            DateTimeOffset.UtcNow);
        handler(this, errorArgs);
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

    private IDisposable RegisterCleanupCore(CleanupPhase phase, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var registration = new CleanupRegistration(this, phase, callback);
        registration.Attach(GetCleanupRegistrations(phase).AddLast(registration));
        return registration;
    }

    private LinkedList<CleanupRegistration> GetCleanupRegistrations(CleanupPhase phase)
    {
        return phase switch
        {
            CleanupPhase.PreDestroy => _preDestroyCleanupCallbacks,
            CleanupPhase.PostDestroy => _cleanupCallbacks,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unsupported cleanup phase."),
        };
    }

    private void RemoveCleanupRegistration(CleanupRegistration registration)
    {
        var node = registration.Node;
        if (node is not null)
        {
            GetCleanupRegistrations(registration.Phase).Remove(node);
        }

        registration.Detach();
    }

    private static void RunCleanupCallbacks(
        BunRuntime owner,
        LinkedList<CleanupRegistration> callbacks,
        string phase,
        ref List<Exception>? exceptions)
    {
        var node = callbacks.First;
        while (node is not null)
        {
            var next = node.Next;
            callbacks.Remove(node);
            var callback = node.Value.TakeCallback();

            try
            {
                callback?.Invoke();
            }
            catch (Exception ex)
            {
                owner.ReportDiagnostic(BunRuntimeDiagnosticSource.Cleanup, ex);
                exceptions ??= [];
                exceptions.Add(new InvalidOperationException($"BunRuntime {phase} cleanup callback failed.", ex));
            }

            node = next;
        }
    }

    private static void DisposeCleanupResource(
        BunRuntime owner,
        IDisposable? resource,
        string description,
        ref List<Exception>? exceptions)
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            resource.Dispose();
        }
        catch (Exception ex)
        {
            owner.ReportDiagnostic(BunRuntimeDiagnosticSource.Cleanup, ex);
            exceptions ??= [];
            exceptions.Add(new InvalidOperationException($"BunRuntime {description} disposal failed.", ex));
        }
    }

    private enum CleanupPhase
    {
        PreDestroy,
        PostDestroy,
    }

    private sealed class CleanupRegistration : IDisposable
    {
        private BunRuntime? _owner;
        private Action? _callback;

        public CleanupRegistration(BunRuntime owner, CleanupPhase phase, Action callback)
        {
            _owner = owner;
            _callback = callback;
            Phase = phase;
        }

        public CleanupPhase Phase { get; }

        public LinkedListNode<CleanupRegistration>? Node { get; private set; }

        public void Attach(LinkedListNode<CleanupRegistration> node)
        {
            Node = node;
        }

        public Action? TakeCallback()
        {
            var callback = _callback;
            Detach();
            return callback;
        }

        public void Detach()
        {
            _owner = null;
            _callback = null;
            Node = null;
        }

        public void Dispose()
        {
            var owner = _owner;
            if (owner is null)
            {
                return;
            }

            owner.RemoveCleanupRegistration(this);
        }
    }
}

internal sealed class BunObjectFinalizerRegistration : IDisposable
{
    private readonly BunRuntime _owner;
    private readonly BunValue _target;
    private readonly HashSet<BunCallbackHandle> _callbackHandles = [];
    private readonly Dictionary<string, PropertyCallbackRegistration> _propertyCallbacks = new(StringComparer.Ordinal);
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
        var handle = BunManagedCallbackRegistry.CreateFinalizer(_ => registration.OnTargetFinalized(), owner, 0);
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

    internal void ReplaceGetterCallbackHandle(string key, BunCallbackHandle handle)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ref var registration = ref GetOrCreatePropertyRegistration(key);
        ReleaseReplacedGetter(registration);
        registration.GetterHandle = handle;
        _callbackHandles.Add(handle);
    }

    internal void ReplaceSetterCallbackHandle(string key, BunCallbackHandle handle)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ref var registration = ref GetOrCreatePropertyRegistration(key);
        ReleaseReplacedSetter(registration);
        registration.SetterHandle = handle;
        _callbackHandles.Add(handle);
    }

    internal void ReplaceAccessorCallbackHandle(string key, BunManagedGetter? getter, BunManagedSetter? setter, BunCallbackHandle handle)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ref var registration = ref GetOrCreatePropertyRegistration(key);
        ReleaseFullyReplacedPropertyCallbacks(registration);

        registration.GetterHandle = getter is not null ? handle : null;
        registration.SetterHandle = setter is not null ? handle : null;

        if (registration.GetterHandle is null && registration.SetterHandle is null)
        {
            _propertyCallbacks.Remove(key);
            handle.Dispose();
            return;
        }

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

        List<Exception>? cleanupExceptions = null;

        if (runManagedFinalizers && _hasManagedFinalizer)
        {
            try
            {
                _managedFinalizer!(_managedFinalizerUserData);
            }
            catch (Exception ex)
            {
                // Object finalizers run while runtime-owned cleanup is already in progress.
                // Report through the runtime and continue releasing remaining resources.
                _owner.ReportDiagnostic(BunRuntimeDiagnosticSource.ObjectFinalizer, ex);
            }
        }

        _propertyCallbacks.Clear();
        _managedFinalizer = null;
        _managedFinalizerUserData = 0;
        _hasManagedFinalizer = false;

        // BunCallbackHandle.Dispose() never calls back into _callbackHandles
        // (SetRemoveFromOwner is never set on these handles), so no snapshot needed.
        // _disposed = true guards against re-entry, so iterating then clearing is safe.
        foreach (var handle in _callbackHandles)
        {
            try
            {
                handle.Dispose();
            }
            catch (Exception ex)
            {
                _owner.ReportDiagnostic(BunRuntimeDiagnosticSource.Cleanup, ex);
                cleanupExceptions ??= [];
                cleanupExceptions.Add(new InvalidOperationException("BunObjectFinalizerRegistration callback handle disposal failed.", ex));
            }
        }
        _callbackHandles.Clear();

        if (cleanupExceptions is not null)
        {
            throw new AggregateException("One or more BunObjectFinalizerRegistration cleanup operations failed.", cleanupExceptions);
        }
    }

    private ref PropertyCallbackRegistration GetOrCreatePropertyRegistration(string key)
    {
        CollectionsMarshal.GetValueRefOrAddDefault(_propertyCallbacks, key, out var exists);
        if (!exists)
            _propertyCallbacks[key] = new PropertyCallbackRegistration();

        return ref CollectionsMarshal.GetValueRefOrNullRef(_propertyCallbacks, key);
    }

    private void ReleaseReplacedGetter(PropertyCallbackRegistration registration)
    {
        var oldGetterHandle = registration.GetterHandle;
        if (oldGetterHandle is null)
            return;

        if (ReferenceEquals(oldGetterHandle, registration.SetterHandle))
        {
            if (!BunManagedCallbackRegistry.ClearGetterCallback(oldGetterHandle))
                RemoveAndDisposeCallbackHandle(oldGetterHandle);
            return;
        }

        registration.GetterHandle = null;
        RemoveAndDisposeCallbackHandle(oldGetterHandle);
    }

    private void ReleaseReplacedSetter(PropertyCallbackRegistration registration)
    {
        var oldSetterHandle = registration.SetterHandle;
        if (oldSetterHandle is null)
            return;

        if (ReferenceEquals(oldSetterHandle, registration.GetterHandle))
        {
            if (!BunManagedCallbackRegistry.ClearSetterCallback(oldSetterHandle))
                RemoveAndDisposeCallbackHandle(oldSetterHandle);
            return;
        }

        registration.SetterHandle = null;
        RemoveAndDisposeCallbackHandle(oldSetterHandle);
    }

    private void ReleaseFullyReplacedPropertyCallbacks(PropertyCallbackRegistration registration)
    {
        var oldGetterHandle = registration.GetterHandle;
        var oldSetterHandle = registration.SetterHandle;

        registration.GetterHandle = null;
        registration.SetterHandle = null;

        if (oldGetterHandle is not null)
            RemoveAndDisposeCallbackHandle(oldGetterHandle);

        if (oldSetterHandle is not null && !ReferenceEquals(oldSetterHandle, oldGetterHandle))
            RemoveAndDisposeCallbackHandle(oldSetterHandle);
    }

    private void RemoveAndDisposeCallbackHandle(BunCallbackHandle handle)
    {
        _callbackHandles.Remove(handle);
        handle.Dispose();
    }

    private sealed class PropertyCallbackRegistration
    {
        public BunCallbackHandle? GetterHandle;

        public BunCallbackHandle? SetterHandle;
    }
}