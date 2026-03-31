using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BunSharp.Interop;

namespace BunSharp;

internal static unsafe class BunManagedCallbackRegistry
{
    public static nint HostFunctionPointer => (nint)(delegate* unmanaged[Cdecl]<nint, int, BunValue*, nint, BunValue>)&HostFunctionThunk;

    public static nint FinalizerPointer => (nint)(delegate* unmanaged[Cdecl]<nint, void>)&FinalizerThunk;

    public static nint ClassMethodPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, int, BunValue*, nint, BunValue>)&ClassMethodThunk;

    public static nint ClassGetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, nint, BunValue>)&ClassGetterThunk;

    public static nint ClassSetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, BunValue, nint, void>)&ClassSetterThunk;

    public static nint ClassConstructorPointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, BunValue*, nint, BunValue>)&ClassConstructorThunk;

    public static nint ClassStaticMethodPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, int, BunValue*, BunValue>)&ClassStaticMethodThunk;

    public static nint ClassStaticGetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, nint, BunValue>)&ClassStaticGetterThunk;

    public static nint ClassStaticSetterPointer => (nint)(delegate* unmanaged[Cdecl]<nint, BunValue, BunValue, nint, void>)&ClassStaticSetterThunk;

    public static nint ClassFinalizerPointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&ClassFinalizerThunk;

    public static nint CallbackHandleDisposerPointer => (nint)(delegate* unmanaged[Cdecl]<nint, void>)&CallbackHandleDisposerThunk;

    public static BunCallbackHandle CreateHost(BunManagedHostCallback callback, nint userdata)
    {
        return new BunCallbackHandle(new HostCallbackState(callback, userdata));
    }

    public static BunCallbackHandle CreateGetter(BunManagedGetter callback)
    {
        BunGetterFunction nativeCallback = (context, thisValue) =>
        {
            try
            {
                return callback(new BunContext(context), thisValue);
            }
            catch
            {
                return BunValue.Undefined;
            }
        };

        return new BunCallbackHandle(nativeCallback);
    }

    public static BunCallbackHandle CreateSetter(BunManagedSetter callback)
    {
        BunSetterFunction nativeCallback = (context, thisValue, value) =>
        {
            try
            {
                callback(new BunContext(context), thisValue, value);
            }
            catch
            {
            }
        };

        return new BunCallbackHandle(nativeCallback);
    }

    public static BunCallbackHandle CreateFinalizer(BunManagedFinalizer callback, nint userdata)
    {
        return new BunCallbackHandle(new FinalizerCallbackState(callback, userdata));
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

    public static BunCallbackHandle CreateClassFinalizer(BunManagedClassFinalizer callback, nint userdata)
    {
        return new BunCallbackHandle(new ClassFinalizerCallbackState(callback, userdata));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue HostFunctionThunk(nint context, int argc, BunValue* argv, nint userdata)
    {
        try
        {
            var state = GetState<HostCallbackState>(userdata);
            var args = argc <= 0 || argv == null ? ReadOnlySpan<BunValue>.Empty : new ReadOnlySpan<BunValue>(argv, argc);
            return state.Callback(new BunContext(context), args, state.UserData);
        }
        catch
        {
            return BunValue.Undefined;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FinalizerThunk(nint userdata)
    {
        try
        {
            var state = GetState<FinalizerCallbackState>(userdata);
            state.Callback(state.UserData);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassMethodThunk(nint context, BunValue thisValue, nint nativePtr, int argc, BunValue* argv, nint userdata)
    {
        try
        {
            var state = GetState<ClassMethodCallbackState>(userdata);
            var args = argc <= 0 || argv == null ? ReadOnlySpan<BunValue>.Empty : new ReadOnlySpan<BunValue>(argv, argc);
            return state.Callback(new BunContext(context), thisValue, nativePtr, args, state.UserData);
        }
        catch
        {
            return BunValue.Undefined;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassGetterThunk(nint context, BunValue thisValue, nint nativePtr, nint userdata)
    {
        try
        {
            var state = GetState<ClassPropertyCallbackState>(userdata);
            return state.Getter is null
                ? BunValue.Undefined
                : state.Getter(new BunContext(context), thisValue, nativePtr, state.UserData);
        }
        catch
        {
            return BunValue.Undefined;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ClassSetterThunk(nint context, BunValue thisValue, nint nativePtr, BunValue value, nint userdata)
    {
        try
        {
            var state = GetState<ClassPropertyCallbackState>(userdata);
            state.Setter?.Invoke(new BunContext(context), thisValue, nativePtr, value, state.UserData);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassConstructorThunk(nint context, nint klass, int argc, BunValue* argv, nint userdata)
    {
        try
        {
            var state = GetState<ClassConstructorCallbackState>(userdata);
            var args = argc <= 0 || argv == null ? ReadOnlySpan<BunValue>.Empty : new ReadOnlySpan<BunValue>(argv, argc);
            return state.Callback(new BunContext(context), klass, args, state.UserData);
        }
        catch
        {
            return BunValue.Undefined;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassStaticMethodThunk(nint context, BunValue thisValue, nint userdata, int argc, BunValue* argv)
    {
        try
        {
            var state = GetState<ClassStaticMethodCallbackState>(userdata);
            var args = argc <= 0 || argv == null ? ReadOnlySpan<BunValue>.Empty : new ReadOnlySpan<BunValue>(argv, argc);
            return state.Callback(new BunContext(context), thisValue, args, state.UserData);
        }
        catch
        {
            return BunValue.Undefined;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static BunValue ClassStaticGetterThunk(nint context, BunValue thisValue, nint userdata)
    {
        try
        {
            var state = GetState<ClassStaticPropertyCallbackState>(userdata);
            return state.Getter is null
                ? BunValue.Undefined
                : state.Getter(new BunContext(context), thisValue, state.UserData);
        }
        catch
        {
            return BunValue.Undefined;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ClassStaticSetterThunk(nint context, BunValue thisValue, BunValue value, nint userdata)
    {
        try
        {
            var state = GetState<ClassStaticPropertyCallbackState>(userdata);
            state.Setter?.Invoke(new BunContext(context), thisValue, value, state.UserData);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ClassFinalizerThunk(nint nativePtr, nint userdata)
    {
        try
        {
            var state = GetState<ClassFinalizerCallbackState>(userdata);
            state.Callback(nativePtr, state.UserData);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CallbackHandleDisposerThunk(nint userdata)
    {
        try
        {
            if (userdata != 0)
            {
                var handle = GCHandle.FromIntPtr(userdata);
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }
        catch
        {
        }
    }

    private static TState GetState<TState>(nint userdata)
        where TState : class
    {
        var handle = GCHandle.FromIntPtr(userdata);
        return (TState)handle.Target!;
    }

    private sealed record HostCallbackState(BunManagedHostCallback Callback, nint UserData);

    private sealed record FinalizerCallbackState(BunManagedFinalizer Callback, nint UserData);

    private sealed record ClassMethodCallbackState(BunManagedClassMethod Callback, nint UserData);

    private sealed record ClassPropertyCallbackState(BunManagedClassGetter? Getter, BunManagedClassSetter? Setter, nint UserData);

    private sealed record ClassConstructorCallbackState(BunManagedClassConstructor Callback, nint UserData);

    private sealed record ClassStaticMethodCallbackState(BunManagedClassStaticMethod Callback, nint UserData);

    private sealed record ClassStaticPropertyCallbackState(BunManagedClassStaticGetter? Getter, BunManagedClassStaticSetter? Setter, nint UserData);

    private sealed record ClassFinalizerCallbackState(BunManagedClassFinalizer Callback, nint UserData);
}

internal sealed class BunCallbackHandle : IDisposable
{
    private GCHandle _handle;
    private readonly Delegate? _delegate;

    public BunCallbackHandle(object state)
    {
        _handle = GCHandle.Alloc(state, GCHandleType.Normal);
        Pointer = GCHandle.ToIntPtr(_handle);
    }

    public BunCallbackHandle(Delegate callback)
    {
        _delegate = callback;
        Pointer = Marshal.GetFunctionPointerForDelegate(callback);
    }

    public nint Pointer { get; }

    public void Dispose()
    {
        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }
}