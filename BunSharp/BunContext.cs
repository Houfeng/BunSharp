using System.Runtime.InteropServices;
using BunSharp.Interop;

namespace BunSharp;

public unsafe sealed class BunContext
{
    private readonly BunRuntime? _runtime;

    internal BunContext(nint handle)
    {
        Handle = handle;
    }

    internal BunContext(BunRuntime runtime, nint handle)
    {
        _runtime = runtime;
        Handle = handle;
    }

    public nint Handle { get; }

    public BunValue GlobalObject
    {
        get
        {
            VerifyThread();
            return BunNative.Global(Handle);
        }
    }

    public void Evaluate(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        VerifyThread();

        var result = BunNative.EvalString(Handle, code);
        ThrowIfEvaluationFailed(result, "bun_eval_string failed.");
    }

    public void EvaluateFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        VerifyThread();

        var result = BunNative.EvalFile(Handle, path);
        ThrowIfEvaluationFailed(result, "bun_eval_file failed.");
    }

    public BunValue EvaluateExpression(string code)
    {
        var tmpVarName = $"__temp_result_{Guid.NewGuid():N}__";
        Evaluate($"globalThis.{tmpVarName} = ({code});");
        var result = GetProperty(GlobalObject, tmpVarName);
        SetProperty(GlobalObject, tmpVarName, BunValue.Undefined);
        return result;
    }

    public BunValue CreateBoolean(bool value) => BunNative.Bool(value ? 1 : 0);

    public BunValue CreateNumber(double value) => BunNative.Number(value);

    public BunValue CreateInt32(int value) => BunNative.Int32(value);

    public BunValue CreateString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        VerifyThread();
        return BunNative.CreateString(Handle, value);
    }

    public BunValue CreateObject()
    {
        VerifyThread();
        return BunNative.Object(Handle);
    }

    public BunValue CreateArray(nuint length)
    {
        VerifyThread();
        return BunNative.Array(Handle, length);
    }

    public BunValue CreateFunction(string name, BunManagedHostCallback callback, int argCount = 0, nint userdata = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(callback);
        VerifyThread();

        var owner = GetOwningRuntime();
        var handle = BunManagedCallbackRegistry.CreateHost(callback, userdata);
        try
        {
            var function = BunNative.Function(Handle, name, BunManagedCallbackRegistry.HostFunctionPointer, handle.Pointer, argCount);
            var disposerHandle = GCHandle.Alloc(handle);
            var disposerPtr = GCHandle.ToIntPtr(disposerHandle);
            handle.SetDisposerHandle(disposerPtr);
            BunNative.DefineFinalizer(Handle, function, BunManagedCallbackRegistry.CallbackHandleDisposerPointer, disposerPtr);
            owner.Retain(handle);
            return function;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public BunValue CreateArrayBuffer(nint data, nuint length, BunManagedFinalizer? finalizer = null, nint userdata = 0)
    {
        VerifyThread();
        return CreateMemoryBackedValue(data, length, finalizer, userdata, static (context, pointer, size, finalizerPtr, userData) => BunNative.ArrayBuffer(context, pointer, size, finalizerPtr, userData));
    }

    public BunValue CreateTypedArray(BunTypedArrayKind kind, nint data, nuint elementCount, BunManagedFinalizer? finalizer = null, nint userdata = 0)
    {
        VerifyThread();
        return CreateMemoryBackedValue(data, elementCount, finalizer, userdata, (context, pointer, size, finalizerPtr, userData) => BunNative.TypedArray(context, kind, pointer, size, finalizerPtr, userData));
    }

    public bool TryGetArrayBuffer(BunValue value, out BunArrayBufferInfo info)
    {
        VerifyThread();
        return BunNative.GetArrayBuffer(Handle, value, out info) != 0;
    }

    public bool TryGetTypedArray(BunValue value, out BunTypedArrayInfo info)
    {
        VerifyThread();
        return BunNative.GetTypedArray(Handle, value, out info) != 0;
    }

    public bool SetProperty(BunValue target, string key, BunValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        VerifyThread();
        return BunNative.Set(Handle, target, key, value) != 0;
    }

    public BunValue GetProperty(BunValue target, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        VerifyThread();
        return BunNative.Get(Handle, target, key);
    }

    public bool SetIndex(BunValue target, uint index, BunValue value)
    {
        VerifyThread();
        return BunNative.SetIndex(Handle, target, index, value) != 0;
    }

    public BunValue GetIndex(BunValue target, uint index)
    {
        VerifyThread();
        return BunNative.GetIndex(Handle, target, index);
    }

    public bool DefineGetter(BunValue target, string key, BunManagedGetter getter, bool dontEnum = false, bool dontDelete = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(getter);
        VerifyThread();

        var owner = GetOwningRuntime();
        var handle = BunManagedCallbackRegistry.CreateGetter(getter);
        try
        {
            var result = BunNative.DefineGetter(Handle, target, key, handle.Pointer, dontEnum ? 1 : 0, dontDelete ? 1 : 0) != 0;
            owner.Retain(handle);
            return result;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public bool DefineSetter(BunValue target, string key, BunManagedSetter setter, bool dontEnum = false, bool dontDelete = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(setter);
        VerifyThread();

        var owner = GetOwningRuntime();
        var handle = BunManagedCallbackRegistry.CreateSetter(setter);
        try
        {
            var result = BunNative.DefineSetter(Handle, target, key, handle.Pointer, dontEnum ? 1 : 0, dontDelete ? 1 : 0) != 0;
            owner.Retain(handle);
            return result;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public bool DefineAccessor(BunValue target, string key, BunManagedGetter? getter = null, BunManagedSetter? setter = null, bool readOnly = false, bool dontEnum = false, bool dontDelete = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        VerifyThread();

        var owner = GetOwningRuntime();
        BunCallbackHandle? getterHandle = null;
        BunCallbackHandle? setterHandle = null;

        try
        {
            if (getter is not null)
            {
                getterHandle = BunManagedCallbackRegistry.CreateGetter(getter);
            }

            if (setter is not null)
            {
                setterHandle = BunManagedCallbackRegistry.CreateSetter(setter);
            }

            var result = BunNative.DefineAccessor(
                Handle,
                target,
                key,
                getterHandle?.Pointer ?? 0,
                setterHandle?.Pointer ?? 0,
                readOnly ? 1 : 0,
                dontEnum ? 1 : 0,
                dontDelete ? 1 : 0) != 0;

            if (getterHandle is not null)
            {
                owner.Retain(getterHandle);
                getterHandle = null;
            }

            if (setterHandle is not null)
            {
                owner.Retain(setterHandle);
                setterHandle = null;
            }

            return result;
        }
        finally
        {
            getterHandle?.Dispose();
            setterHandle?.Dispose();
        }
    }

    public bool DefineFinalizer(BunValue target, BunManagedFinalizer finalizer, nint userdata = 0)
    {
        ArgumentNullException.ThrowIfNull(finalizer);
        VerifyThread();

        var owner = GetOwningRuntime();
        var handle = BunManagedCallbackRegistry.CreateFinalizer(finalizer, userdata);
        try
        {
            var result = BunNative.DefineFinalizer(Handle, target, BunManagedCallbackRegistry.FinalizerPointer, handle.Pointer) != 0;
            owner.Retain(handle);
            return result;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public bool SetPrototype(BunValue target, BunValue prototype)
    {
        VerifyThread();
        return BunNative.SetPrototype(Handle, target, prototype) != 0;
    }

    public void SetOpaque(BunValue target, nint opaquePtr)
    {
        VerifyThread();
        BunNative.SetOpaque(Handle, target, opaquePtr);
    }

    public nint GetOpaque(BunValue target)
    {
        VerifyThread();
        return BunNative.GetOpaque(Handle, target);
    }

    public bool IsUndefined(BunValue value) => BunNative.IsUndefined(value) != 0;

    public bool IsNull(BunValue value) => BunNative.IsNull(value) != 0;

    public bool IsBool(BunValue value) => BunNative.IsBool(value) != 0;

    public bool IsNumber(BunValue value) => BunNative.IsNumber(value) != 0;

    public bool IsString(BunValue value) => BunNative.IsString(value) != 0;

    public bool IsObject(BunValue value) => BunNative.IsObject(value) != 0;

    public bool IsCallable(BunValue value) => BunNative.IsCallable(value) != 0;

    public bool ToBoolean(BunValue value) => BunNative.ToBool(value) != 0;

    public double ToNumber(BunValue value)
    {
        VerifyThread();
        return BunNative.ToNumber(Handle, value);
    }

    public int ToInt32(BunValue value) => BunNative.ToInt32(value);

    public string? ToManagedString(BunValue value)
    {
        VerifyThread();
        var pointer = BunNative.ToUtf8(Handle, value, out _);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            return BunNative.CopyUtf8String(pointer);
        }
        finally
        {
            CRuntime.Free(pointer);
        }
    }

    public BunValue Call(BunValue function, BunValue thisValue, ReadOnlySpan<BunValue> args)
    {
        VerifyThread();

        if (args.IsEmpty)
        {
            return HandleCallResult(BunNative.Call(Handle, function, thisValue, 0, null));
        }

        fixed (BunValue* argsPointer = args)
        {
            return HandleCallResult(BunNative.Call(Handle, function, thisValue, args.Length, argsPointer));
        }
    }

    public int CallAsync(BunValue function, BunValue thisValue, ReadOnlySpan<BunValue> args)
    {
        VerifyThread();

        if (args.IsEmpty)
        {
            return BunNative.CallAsync(Handle, function, thisValue, 0, null);
        }

        fixed (BunValue* argsPointer = args)
        {
            return BunNative.CallAsync(Handle, function, thisValue, args.Length, argsPointer);
        }
    }

    public void Protect(BunValue value)
    {
        VerifyThread();
        BunNative.Protect(Handle, value);
    }

    public void Unprotect(BunValue value)
    {
        VerifyThread();
        BunNative.Unprotect(Handle, value);
    }

    public string? GetLastError()
    {
        VerifyThread();
        return BunNative.CopyUtf8String(BunNative.LastError(Handle));
    }

    public BunClass RegisterClass(BunClassDefinition definition, BunClass? parent = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        VerifyThread();

        var owner = GetOwningRuntime();
        var prepared = PreparedClassDefinition.Create(definition);
        try
        {
            var descriptor = prepared.Descriptor;
            var handle = BunNative.ClassRegister(Handle, in descriptor, parent?.Handle ?? 0);
            if (handle == 0)
            {
                throw new BunException("bun_class_register returned a null class handle.");
            }

            var bunClass = new BunClass(owner, handle, prepared);
            owner.Retain(bunClass);
            prepared = null;
            return bunClass;
        }
        finally
        {
            prepared?.Dispose();
        }
    }

    private BunValue CreateMemoryBackedValue(nint data, nuint size, BunManagedFinalizer? finalizer, nint userdata, Func<nint, nint, nuint, nint, nint, BunValue> factory)
    {
        if (finalizer is null)
        {
            return factory(Handle, data, size, 0, userdata);
        }

        var owner = GetOwningRuntime();
        var handle = BunManagedCallbackRegistry.CreateFinalizer(finalizer, userdata);
        try
        {
            var value = factory(Handle, data, size, BunManagedCallbackRegistry.FinalizerPointer, handle.Pointer);
            owner.Retain(handle);
            return value;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private BunValue HandleCallResult(BunValue value)
    {
        if (!value.IsException)
        {
            return value;
        }

        throw new BunException(GetLastError() ?? "bun_call failed with a JavaScript exception.");
    }

    private static void ThrowIfEvaluationFailed(BunEvalResult result, string fallbackMessage)
    {
        if (result.Success == 0)
        {
            throw new BunException(result.ErrorMessage ?? fallbackMessage);
        }
    }

    private BunRuntime GetOwningRuntime()
    {
        return _runtime ?? throw new InvalidOperationException("This BunContext was created from an unmanaged callback and cannot retain managed resources.");
    }

    public void RegisterCleanup(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        GetOwningRuntime().RegisterCleanup(callback);
    }

    private void VerifyThread()
    {
        _runtime?.VerifyThread();
    }
}