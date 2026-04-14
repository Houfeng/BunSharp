using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BunSharp.Interop;

namespace BunSharp;

internal static unsafe class BunManagedCallbackRegistry
{
    private static readonly object s_contextOwnerSyncRoot = new();
    private static readonly Dictionary<nint, BunRuntime> s_contextOwners = [];

    public static nint HostFunctionPointer => (nint)(delegate* unmanaged[Cdecl]<nint, int, BunValue*, nint, BunValue>)&HostFunctionThunk;

    public static nint EventCallbackPointer => (nint)(delegate* unmanaged[Cdecl]<nint, void>)&EventCallbackThunk;

    public static nint FinalizerPointer => (nint)(delegate* unmanaged[Cdecl]<nint, void>)&FinalizerThunk;

    public static nint ClassMethodPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, int, BunValue*, nint, BunValue>)&ClassMethodThunk;

    public static nint ClassGetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, nint, BunValue>)&ClassGetterThunk;

    public static nint ClassSetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, BunValue, nint, void>)&ClassSetterThunk;

    public static nint ClassConstructorPointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, BunValue*, nint, BunValue>)&ClassConstructorThunk;

    public static nint ClassStaticMethodPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, int, BunValue*, BunValue>)&ClassStaticMethodThunk;

    public static nint ClassStaticGetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, BunValue>)&ClassStaticGetterThunk;

    public static nint ClassStaticSetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, BunValue, nint, void>)&ClassStaticSetterThunk;

    public static nint ClassFinalizerPointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&ClassFinalizerThunk;

    public static nint PersistentClassFinalizerPointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&PersistentClassFinalizerThunk;

    public static nint CallbackHandleDisposerPointer => (nint)(delegate* unmanaged[Cdecl]<nint, void>)&CallbackHandleDisposerThunk;

    public static nint GetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, BunValue>)&GetterThunk;

    public static nint SetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, BunValue, nint, void>)&SetterThunk;

    // Single-slot thread-local cache for BunContext.
    // Bun runs on a single thread, so each thread sees at most one context handle
    // in the vast majority of cases. A direct nint comparison is orders of magnitude
    // cheaper than a Dictionary hash lookup on every JS→C# callback.
    [ThreadStatic]
    private static nint t_cachedContextHandle;

    [ThreadStatic]
    private static BunContext? t_cachedContext;

    private static BunContext GetOrCreateContext(nint handle)
    {
        // Fast path: handle matches the cached slot — return immediately with no allocation.
        if (t_cachedContextHandle == handle && t_cachedContext is not null)
            return t_cachedContext;

        // Slow path: first call on this thread, or a different context handle.
        // Resolve the owning runtime when available so callback-created contexts can retain managed resources.
        var ctx = TryGetContextOwner(handle, out var owner)
            ? new BunContext(owner, handle)
            : new BunContext(handle);
        t_cachedContextHandle = handle;
        t_cachedContext = ctx;
        return ctx;
    }

    internal static void RegisterContextOwner(nint handle, BunRuntime owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (s_contextOwnerSyncRoot)
        {
            s_contextOwners[handle] = owner;
        }
    }

    private static bool TryGetContextOwner(nint handle, out BunRuntime owner)
    {
        lock (s_contextOwnerSyncRoot)
        {
            return s_contextOwners.TryGetValue(handle, out owner!);
        }
    }

    internal static void removeContextCache(nint handle)
    {
        // Clear the slot when the runtime owning this context is disposed,
        // so a future runtime reusing the same handle value doesn't get a stale wrapper.
        if (t_cachedContextHandle == handle)
        {
            t_cachedContextHandle = 0;
            t_cachedContext = null;
        }

        lock (s_contextOwnerSyncRoot)
        {
            s_contextOwners.Remove(handle);
        }
    }

    public static BunCallbackHandle CreateHost(BunManagedHostCallback callback, nint userdata)
    {
        return new BunCallbackHandle(new HostCallbackState(callback, userdata));
    }

    public static BunCallbackHandle CreateEventCallback(BunManagedEventCallback callback, BunRuntime runtime, nint userdata)
    {
        return new BunCallbackHandle(new EventCallbackState(callback, runtime, userdata));
    }

    public static BunCallbackHandle CreateGetter(BunManagedGetter callback)
    {
        return new BunCallbackHandle(new GetterSetterCallbackState(callback, null));
    }

    public static BunCallbackHandle CreateSetter(BunManagedSetter callback)
    {
        return new BunCallbackHandle(new GetterSetterCallbackState(null, callback));
    }

    public static BunCallbackHandle CreateAccessor(BunManagedGetter? getter, BunManagedSetter? setter)
    {
        return new BunCallbackHandle(new GetterSetterCallbackState(getter, setter));
    }

    internal static bool ClearGetterCallback(BunCallbackHandle handle)
    {
        var state = handle.TryGetState<GetterSetterCallbackState>();
        if (state is null)
            return false;

        state.Getter = null;
        return state.Setter is not null;
    }

    internal static bool ClearSetterCallback(BunCallbackHandle handle)
    {
        var state = handle.TryGetState<GetterSetterCallbackState>();
        if (state is null)
            return false;

        state.Setter = null;
        return state.Getter is not null;
    }

    public static BunCallbackHandle CreateFinalizer(BunManagedFinalizer callback, BunRuntime runtime, nint userdata)
    {
        return new BunCallbackHandle(new FinalizerCallbackState(callback, runtime, userdata), runtime);
    }

    public static BunCallbackHandle CreateClassMethod(BunManagedClassMethod callback, nint userdata)
    {
        return new BunCallbackHandle(new ClassMethodCallbackState(callback, userdata));
    }

    public static BunCallbackHandle CreateClassProperty(BunManagedClassGetter? getter, BunManagedClassSetter? setter, nint userdata)
    {
        return new BunCallbackHandle(new ClassPropertyCallbackState(getter, setter, userdata));
    }

    public static BunCallbackHandle CreateClassConstructor(BunManagedClassConstructor callback, nint userdata)
    {
        return new BunCallbackHandle(new ClassConstructorCallbackState(callback, userdata));
    }

    public static BunCallbackHandle CreateClassStaticMethod(BunManagedClassStaticMethod callback, nint userdata)
    {
        return new BunCallbackHandle(new ClassStaticMethodCallbackState(callback, userdata));
    }

    public static BunCallbackHandle CreateClassStaticProperty(BunManagedClassStaticGetter? getter, BunManagedClassStaticSetter? setter, nint userdata)
    {
        return new BunCallbackHandle(new ClassStaticPropertyCallbackState(getter, setter, userdata));
    }

    public static BunCallbackHandle CreateClassFinalizer(BunManagedClassFinalizer callback, BunRuntime runtime, nint userdata)
    {
        return new BunCallbackHandle(new ClassFinalizerCallbackState(callback, runtime, userdata), runtime);
    }

    public static BunCallbackHandle CreatePersistentClassFinalizer(BunManagedClassFinalizer callback, BunRuntime runtime, nint userdata)
    {
        return new BunCallbackHandle(new ClassFinalizerCallbackState(callback, runtime, userdata), runtime);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue HostFunctionThunk(nint context, int argc, BunValue* argv, nint userdata)
    {
        if (userdata == 0)
        {
            return BunValue.Undefined;
        }

        try
        {
            var state = GetState<HostCallbackState>(userdata);
            var args = argc <= 0 || argv == null ? ReadOnlySpan<BunValue>.Empty : new ReadOnlySpan<BunValue>(argv, argc);
            return state.Callback(GetOrCreateContext(context), args, state.UserData);
        }
        catch (Exception exception)
        {
            return ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void EventCallbackThunk(nint userdata)
    {
        if (userdata == 0)
        {
            return;
        }

        try
        {
            var state = GetState<EventCallbackState>(userdata);
            state.Callback(state.Runtime, state.UserData);
        }
        catch (Exception ex)
        {
            if (TryGetState<EventCallbackState>(userdata, out var state))
            {
                // This callback is entered from a native event-loop notification path.
                // Report through the runtime instead of rethrowing across the unmanaged boundary.
                ReportDiagnostic(state!.Runtime, BunRuntimeDiagnosticSource.EventCallback, ex);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FinalizerThunk(nint userdata)
    {
        if (userdata == 0)
        {
            return;
        }

        try
        {
            var outerHandle = GCHandle.FromIntPtr(userdata);
            if (outerHandle.Target is not BunCallbackHandle callbackHandle) { outerHandle.Free(); return; }
            var state = GetState<FinalizerCallbackState>(callbackHandle.Pointer);
            try { state.Callback(state.UserData); }
            catch (Exception ex)
            {
                // Finalizer callbacks run from native-owned teardown paths.
                // Report through the runtime instead of rethrowing across the unmanaged boundary.
                ReportDiagnostic(state.Runtime, BunRuntimeDiagnosticSource.Finalizer, ex);
            }

            try { callbackHandle.Dispose(); }
            catch (Exception ex)
            {
                // Finalizer handle disposal is best-effort during native teardown.
                // Report through the runtime instead of rethrowing across the unmanaged boundary.
                ReportDiagnostic(state.Runtime, BunRuntimeDiagnosticSource.Finalizer, ex);
            }
        }
        catch (Exception ex)
        {
            // If the outer handle is still intact, surface the failure through the runtime.
            // There is no safe managed caller here that can observe a rethrow directly.
            TryReportOuterHandleDiagnostic(userdata, BunRuntimeDiagnosticSource.Finalizer, ex);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassMethodThunk(nint context, BunValue thisValue, nint nativePtr, int argc, BunValue* argv, nint userdata)
    {
        if (userdata == 0)
        {
            return BunValue.Undefined;
        }

        try
        {
            var state = GetState<ClassMethodCallbackState>(userdata);
            var args = argc <= 0 || argv == null ? ReadOnlySpan<BunValue>.Empty : new ReadOnlySpan<BunValue>(argv, argc);
            return state.Callback(GetOrCreateContext(context), thisValue, nativePtr, args, state.UserData);
        }
        catch (Exception exception)
        {
            return ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassGetterThunk(nint context, BunValue thisValue, nint nativePtr, nint userdata)
    {
        if (userdata == 0)
        {
            return BunValue.Undefined;
        }

        try
        {
            var state = GetState<ClassPropertyCallbackState>(userdata);
            return state.Getter is null
                ? BunValue.Undefined
                : state.Getter(GetOrCreateContext(context), thisValue, nativePtr, state.UserData);
        }
        catch (Exception exception)
        {
            return ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ClassSetterThunk(nint context, BunValue thisValue, nint nativePtr, BunValue value, nint userdata)
    {
        if (userdata == 0)
        {
            return;
        }

        try
        {
            var state = GetState<ClassPropertyCallbackState>(userdata);
            state.Setter?.Invoke(GetOrCreateContext(context), thisValue, nativePtr, value, state.UserData);
        }
        catch (Exception exception)
        {
            ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassConstructorThunk(nint context, nint klass, int argc, BunValue* argv, nint userdata)
    {
        if (userdata == 0)
        {
            return BunValue.Undefined;
        }

        try
        {
            var state = GetState<ClassConstructorCallbackState>(userdata);
            var args = argc <= 0 || argv == null ? ReadOnlySpan<BunValue>.Empty : new ReadOnlySpan<BunValue>(argv, argc);
            return state.Callback(GetOrCreateContext(context), klass, args, state.UserData);
        }
        catch (Exception exception)
        {
            return ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassStaticMethodThunk(nint context, BunValue thisValue, nint userdata, int argc, BunValue* argv)
    {
        if (userdata == 0)
        {
            return BunValue.Undefined;
        }

        try
        {
            var state = GetState<ClassStaticMethodCallbackState>(userdata);
            var args = argc <= 0 || argv == null ? ReadOnlySpan<BunValue>.Empty : new ReadOnlySpan<BunValue>(argv, argc);
            return state.Callback(GetOrCreateContext(context), thisValue, args, state.UserData);
        }
        catch (Exception exception)
        {
            return ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassStaticGetterThunk(nint context, BunValue thisValue, nint userdata)
    {
        if (userdata == 0)
        {
            return BunValue.Undefined;
        }

        try
        {
            var state = GetState<ClassStaticPropertyCallbackState>(userdata);
            return state.Getter is null
                ? BunValue.Undefined
                : state.Getter(GetOrCreateContext(context), thisValue, state.UserData);
        }
        catch (Exception exception)
        {
            return ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ClassStaticSetterThunk(nint context, BunValue thisValue, BunValue value, nint userdata)
    {
        if (userdata == 0)
        {
            return;
        }

        try
        {
            var state = GetState<ClassStaticPropertyCallbackState>(userdata);
            state.Setter?.Invoke(GetOrCreateContext(context), thisValue, value, state.UserData);
        }
        catch (Exception exception)
        {
            ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ClassFinalizerThunk(nint nativePtr, nint userdata)
    {
        if (userdata == 0)
        {
            return;
        }

        try
        {
            var outerHandle = GCHandle.FromIntPtr(userdata);
            if (outerHandle.Target is not BunCallbackHandle callbackHandle) { outerHandle.Free(); return; }
            var state = GetState<ClassFinalizerCallbackState>(callbackHandle.Pointer);
            try { state.Callback(nativePtr, state.UserData); }
            catch (Exception ex)
            {
                // Class finalizers run from native-owned teardown paths.
                // Report through the runtime instead of rethrowing across the unmanaged boundary.
                ReportDiagnostic(state.Runtime, BunRuntimeDiagnosticSource.ClassFinalizer, ex);
            }

            try { callbackHandle.Dispose(); }
            catch (Exception ex)
            {
                // Finalizer handle disposal is best-effort during native teardown.
                // Report through the runtime instead of rethrowing across the unmanaged boundary.
                ReportDiagnostic(state.Runtime, BunRuntimeDiagnosticSource.ClassFinalizer, ex);
            }
        }
        catch (Exception ex)
        {
            // If the outer handle is still intact, surface the failure through the runtime.
            // There is no safe managed caller here that can observe a rethrow directly.
            TryReportOuterHandleDiagnostic(userdata, BunRuntimeDiagnosticSource.ClassFinalizer, ex);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PersistentClassFinalizerThunk(nint nativePtr, nint userdata)
    {
        if (userdata == 0)
        {
            return;
        }

        try
        {
            var state = GetState<ClassFinalizerCallbackState>(userdata);
            try { state.Callback(nativePtr, state.UserData); }
            catch (Exception ex)
            {
                // Persistent class finalizers run from native-owned teardown paths.
                // Report through the runtime instead of rethrowing across the unmanaged boundary.
                ReportDiagnostic(state.Runtime, BunRuntimeDiagnosticSource.PersistentClassFinalizer, ex);
            }
        }
        catch (Exception ex)
        {
            if (TryGetState<ClassFinalizerCallbackState>(userdata, out var state))
            {
                // If state recovery still succeeds, surface the failure through the runtime.
                // There is no safe managed caller here that can observe a rethrow directly.
                ReportDiagnostic(state!.Runtime, BunRuntimeDiagnosticSource.PersistentClassFinalizer, ex);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue GetterThunk(nint context, BunValue thisValue, nint userdata)
    {
        if (userdata == 0)
        {
            return BunValue.Undefined;
        }

        try
        {
            var state = GetState<GetterSetterCallbackState>(userdata);
            return state.Getter is null ? BunValue.Undefined : state.Getter(GetOrCreateContext(context), thisValue);
        }
        catch (Exception exception)
        {
            return ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetterThunk(nint context, BunValue thisValue, BunValue value, nint userdata)
    {
        if (userdata == 0)
        {
            return;
        }

        try
        {
            var state = GetState<GetterSetterCallbackState>(userdata);
            state.Setter?.Invoke(GetOrCreateContext(context), thisValue, value);
        }
        catch (Exception exception)
        {
            ThrowManagedException(context, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CallbackHandleDisposerThunk(nint userdata)
    {
        if (userdata == 0)
        {
            return;
        }

        try
        {
            var outerHandle = GCHandle.FromIntPtr(userdata);
            if (outerHandle.Target is BunCallbackHandle callbackHandle)
            {
                callbackHandle.Dispose();
            }
            else
            {
                outerHandle.Free();
            }
        }
        catch (Exception ex)
        {
            // Native disposer callbacks have no managed caller that can safely observe a rethrow.
            // Report through the runtime when the callback handle still carries runtime ownership.
            TryReportOuterHandleDiagnostic(userdata, BunRuntimeDiagnosticSource.Cleanup, ex);
        }
    }

    private static TState GetState<TState>(nint userdata)
        where TState : class
    {
        var handle = GCHandle.FromIntPtr(userdata);
        return (TState)handle.Target!;
    }

    private static bool TryGetState<TState>(nint userdata, out TState? state)
        where TState : class
    {
        if (userdata == 0)
        {
            state = null;
            return false;
        }

        var handle = GCHandle.FromIntPtr(userdata);
        state = handle.Target as TState;
        return state is not null;
    }

    private static void TryReportOuterHandleDiagnostic(nint userdata, BunRuntimeDiagnosticSource source, Exception exception)
    {
        if (userdata == 0)
        {
            return;
        }

        var outerHandle = GCHandle.FromIntPtr(userdata);
        if (outerHandle.Target is BunCallbackHandle callbackHandle && callbackHandle.TryGetRuntime(out var runtime))
        {
            ReportDiagnostic(runtime, source, exception);
        }
    }

    private static void ReportDiagnostic(BunRuntime runtime, BunRuntimeDiagnosticSource source, Exception exception)
    {
        runtime.ReportDiagnostic(source, exception);
    }

    private static BunValue ThrowManagedException(nint context, Exception exception)
    {
        var error = BunNative.CreateError(context, FormatManagedException(exception));
        return error.IsException ? BunValue.Exception : BunNative.Throw(context, error);
    }

    private static string FormatManagedException(Exception exception)
    {
        if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
        {
            exception = aggregate.InnerExceptions[0];
        }

        var message = exception.Message;
        var typeName = exception.GetType().Name;
        return string.IsNullOrWhiteSpace(message) ? typeName : $"{typeName}: {message}";
    }

    private sealed record HostCallbackState(BunManagedHostCallback Callback, nint UserData);

    private sealed record EventCallbackState(BunManagedEventCallback Callback, BunRuntime Runtime, nint UserData);

    private sealed record FinalizerCallbackState(BunManagedFinalizer Callback, BunRuntime Runtime, nint UserData);

    private sealed record ClassMethodCallbackState(BunManagedClassMethod Callback, nint UserData);

    private sealed record ClassPropertyCallbackState(BunManagedClassGetter? Getter, BunManagedClassSetter? Setter, nint UserData);

    private sealed record ClassConstructorCallbackState(BunManagedClassConstructor Callback, nint UserData);

    private sealed record ClassStaticMethodCallbackState(BunManagedClassStaticMethod Callback, nint UserData);

    private sealed record ClassStaticPropertyCallbackState(BunManagedClassStaticGetter? Getter, BunManagedClassStaticSetter? Setter, nint UserData);

    private sealed record ClassFinalizerCallbackState(BunManagedClassFinalizer Callback, BunRuntime Runtime, nint UserData);

    internal sealed class GetterSetterCallbackState
    {
        public GetterSetterCallbackState(BunManagedGetter? getter, BunManagedSetter? setter)
        {
            Getter = getter;
            Setter = setter;
        }

        public BunManagedGetter? Getter { get; set; }

        public BunManagedSetter? Setter { get; set; }
    }
}

internal sealed class BunCallbackHandle : IDisposable
{
    private nint _handlePtr;
    private nint _disposerHandlePtr;
    private readonly Delegate? _delegate;
    private readonly BunRuntime? _runtime;
    private Action? _removeFromOwner;

    public BunCallbackHandle(object state)
    {
        var handle = GCHandle.Alloc(state, GCHandleType.Normal);
        _handlePtr = GCHandle.ToIntPtr(handle);
        Pointer = _handlePtr;
    }

    public BunCallbackHandle(Delegate callback)
    {
        _delegate = callback;
        Pointer = Marshal.GetFunctionPointerForDelegate(callback);
    }

    internal BunCallbackHandle(object state, BunRuntime runtime)
        : this(state)
    {
        _runtime = runtime;
    }

    public nint Pointer { get; }

    internal TState? TryGetState<TState>()
        where TState : class
    {
        var ptr = _handlePtr;
        if (ptr == 0)
            return null;

        return GCHandle.FromIntPtr(ptr).Target as TState;
    }

    internal bool TryGetRuntime(out BunRuntime runtime)
    {
        runtime = _runtime!;
        return runtime is not null;
    }

    public void SetDisposerHandle(nint disposerHandlePtr)
    {
        _disposerHandlePtr = disposerHandlePtr;
    }

    internal void SetRemoveFromOwner(Action? action) => _removeFromOwner = action;

    public void Dispose()
    {
        var ptr = Interlocked.Exchange(ref _handlePtr, 0);
        if (ptr != 0)
        {
            GCHandle.FromIntPtr(ptr).Free();
            Interlocked.Exchange(ref _removeFromOwner, null)?.Invoke();
        }

        var disposerPtr = Interlocked.Exchange(ref _disposerHandlePtr, 0);
        if (disposerPtr != 0)
        {
            GCHandle.FromIntPtr(disposerPtr).Free();
        }
    }
}