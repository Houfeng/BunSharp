using System.Runtime.InteropServices;
using System.Text;

namespace BunSharp.Interop;

public static unsafe partial class BunNative
{
    static BunNative()
    {
        BunNativeLibraryResolver.Initialize();
    }

    public static nuint GetUtf8ByteCount(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return checked((nuint)Encoding.UTF8.GetByteCount(value));
    }

    private const int Utf8StackThreshold = 256;

    public static BunValue CreateString(nint context, string value)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        if (count <= Utf8StackThreshold)
        {
            byte* buf = stackalloc byte[count + 1];
            Encoding.UTF8.GetBytes(value, new Span<byte>(buf, count));
            buf[count] = 0;
            return StringCore(context, buf, (nuint)count);
        }
        else
        {
            byte* buf = (byte*)NativeMemory.Alloc((nuint)count + 1);
            try
            {
                Encoding.UTF8.GetBytes(value, new Span<byte>(buf, count));
                buf[count] = 0;
                return StringCore(context, buf, (nuint)count);
            }
            finally { NativeMemory.Free(buf); }
        }
    }

    public static int Set(nint context, BunValue @object, string key, BunValue value)
    {
        int count = Encoding.UTF8.GetByteCount(key);
        if (count <= Utf8StackThreshold)
        {
            byte* buf = stackalloc byte[count + 1];
            Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
            buf[count] = 0;
            return SetCore(context, @object, buf, (nuint)count, value);
        }
        else
        {
            byte* buf = (byte*)NativeMemory.Alloc((nuint)count + 1);
            try
            {
                Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
                buf[count] = 0;
                return SetCore(context, @object, buf, (nuint)count, value);
            }
            finally { NativeMemory.Free(buf); }
        }
    }

    public static BunValue Get(nint context, BunValue @object, string key)
    {
        int count = Encoding.UTF8.GetByteCount(key);
        if (count <= Utf8StackThreshold)
        {
            byte* buf = stackalloc byte[count + 1];
            Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
            buf[count] = 0;
            return GetCore(context, @object, buf, (nuint)count);
        }
        else
        {
            byte* buf = (byte*)NativeMemory.Alloc((nuint)count + 1);
            try
            {
                Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
                buf[count] = 0;
                return GetCore(context, @object, buf, (nuint)count);
            }
            finally { NativeMemory.Free(buf); }
        }
    }

    public static int DefineGetter(nint context, BunValue @object, string key, nint getter, nint userdata, int dontEnum = 0, int dontDelete = 0)
    {
        int count = Encoding.UTF8.GetByteCount(key);
        if (count <= Utf8StackThreshold)
        {
            byte* buf = stackalloc byte[count + 1];
            Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
            buf[count] = 0;
            return DefineGetterCore(context, @object, buf, (nuint)count, getter, userdata, dontEnum, dontDelete);
        }
        else
        {
            byte* buf = (byte*)NativeMemory.Alloc((nuint)count + 1);
            try
            {
                Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
                buf[count] = 0;
                return DefineGetterCore(context, @object, buf, (nuint)count, getter, userdata, dontEnum, dontDelete);
            }
            finally { NativeMemory.Free(buf); }
        }
    }

    public static int DefineSetter(nint context, BunValue @object, string key, nint setter, nint userdata, int dontEnum = 0, int dontDelete = 0)
    {
        int count = Encoding.UTF8.GetByteCount(key);
        if (count <= Utf8StackThreshold)
        {
            byte* buf = stackalloc byte[count + 1];
            Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
            buf[count] = 0;
            return DefineSetterCore(context, @object, buf, (nuint)count, setter, userdata, dontEnum, dontDelete);
        }
        else
        {
            byte* buf = (byte*)NativeMemory.Alloc((nuint)count + 1);
            try
            {
                Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
                buf[count] = 0;
                return DefineSetterCore(context, @object, buf, (nuint)count, setter, userdata, dontEnum, dontDelete);
            }
            finally { NativeMemory.Free(buf); }
        }
    }

    public static int DefineAccessor(nint context, BunValue @object, string key, nint getter, nint setter, nint userdata, int readOnly = 0, int dontEnum = 0, int dontDelete = 0)
    {
        int count = Encoding.UTF8.GetByteCount(key);
        if (count <= Utf8StackThreshold)
        {
            byte* buf = stackalloc byte[count + 1];
            Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
            buf[count] = 0;
            return DefineAccessorCore(context, @object, buf, (nuint)count, getter, setter, userdata, readOnly, dontEnum, dontDelete);
        }
        else
        {
            byte* buf = (byte*)NativeMemory.Alloc((nuint)count + 1);
            try
            {
                Encoding.UTF8.GetBytes(key, new Span<byte>(buf, count));
                buf[count] = 0;
                return DefineAccessorCore(context, @object, buf, (nuint)count, getter, setter, userdata, readOnly, dontEnum, dontDelete);
            }
            finally { NativeMemory.Free(buf); }
        }
    }

    public static BunValue EvalString(nint context, string code)
    {
        int count = Encoding.UTF8.GetByteCount(code);
        if (count <= Utf8StackThreshold)
        {
            byte* buf = stackalloc byte[count + 1];
            Encoding.UTF8.GetBytes(code, new Span<byte>(buf, count));
            buf[count] = 0;
            return EvalStringCore(context, buf);
        }
        else
        {
            byte* buf = (byte*)NativeMemory.Alloc((nuint)count + 1);
            try
            {
                Encoding.UTF8.GetBytes(code, new Span<byte>(buf, count));
                buf[count] = 0;
                return EvalStringCore(context, buf);
            }
            finally { NativeMemory.Free(buf); }
        }
    }

    public static BunValue EvalFile(nint context, string path)
    {
        int count = Encoding.UTF8.GetByteCount(path);
        if (count <= Utf8StackThreshold)
        {
            byte* buf = stackalloc byte[count + 1];
            Encoding.UTF8.GetBytes(path, new Span<byte>(buf, count));
            buf[count] = 0;
            return EvalFileCore(context, buf);
        }
        else
        {
            byte* buf = (byte*)NativeMemory.Alloc((nuint)count + 1);
            try
            {
                Encoding.UTF8.GetBytes(path, new Span<byte>(buf, count));
                buf[count] = 0;
                return EvalFileCore(context, buf);
            }
            finally { NativeMemory.Free(buf); }
        }
    }

    public static BunValue Function(nint context, string name, nint callback, nint userdata, int argCount)
    {
        int count = Encoding.UTF8.GetByteCount(name);
        if (count <= Utf8StackThreshold)
        {
            byte* buf = stackalloc byte[count + 1];
            Encoding.UTF8.GetBytes(name, new Span<byte>(buf, count));
            buf[count] = 0;
            return FunctionCore(context, buf, callback, userdata, argCount);
        }
        else
        {
            byte* buf = (byte*)NativeMemory.Alloc((nuint)count + 1);
            try
            {
                Encoding.UTF8.GetBytes(name, new Span<byte>(buf, count));
                buf[count] = 0;
                return FunctionCore(context, buf, callback, userdata, argCount);
            }
            finally { NativeMemory.Free(buf); }
        }
    }

    public static string? CopyUtf8String(nint pointer)
    {
        return pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);
    }

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_initialize")]
    internal static partial nint Initialize(BunInitializeOptions* options);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_destroy")]
    public static partial void Destroy(nint runtime);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_context")]
    public static partial nint Context(nint runtime);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_eval_string")]
    private static partial BunValue EvalStringCore(nint context, byte* code);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_eval_file")]
    private static partial BunValue EvalFileCore(nint context, byte* path);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_run_pending_jobs")]
    public static partial int RunPendingJobs(nint runtime);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_get_event_fd")]
    public static partial int GetEventFd(nint runtime);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_wakeup")]
    public static partial void Wakeup(nint runtime);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_bool")]
    public static partial BunValue Bool(int value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_number")]
    public static partial BunValue Number(double value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_int32")]
    public static partial BunValue Int32(int value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_string")]
    private static partial BunValue StringCore(nint context, byte* utf8, nuint length);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_object")]
    public static partial BunValue Object(nint context);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_array")]
    public static partial BunValue Array(nint context, nuint length);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_global")]
    public static partial BunValue Global(nint context);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_function")]
    private static partial BunValue FunctionCore(nint context, byte* name, nint callback, nint userdata, int argCount);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_array_buffer")]
    public static partial BunValue ArrayBuffer(nint context, nint data, nuint length, nint finalizer, nint userdata);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_typed_array")]
    public static partial BunValue TypedArray(nint context, BunTypedArrayKind kind, nint data, nuint elementCount, nint finalizer, nint userdata);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_get_array_buffer")]
    public static partial int GetArrayBuffer(nint context, BunValue value, out BunArrayBufferInfo info);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_get_typed_array")]
    public static partial int GetTypedArray(nint context, BunValue value, out BunTypedArrayInfo info);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_class_register")]
    public static partial nint ClassRegister(nint context, in BunClassDescriptor descriptor, nint parent);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_class_new")]
    public static partial BunValue ClassNew(nint context, nint klass, nint nativePtr, nint finalizer, nint userdata);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_class_unwrap")]
    public static partial nint ClassUnwrap(nint context, BunValue value, nint klass);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_is_class_instance")]
    public static partial int IsClassInstance(nint context, BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_instanceof_class")]
    public static partial int InstanceOfClass(nint context, BunValue value, nint klass);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_class_dispose")]
    public static partial int ClassDispose(nint context, BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_class_prototype")]
    public static partial BunValue ClassPrototype(nint context, nint klass);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_class_constructor")]
    public static partial BunValue ClassConstructor(nint context, nint klass);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_is_undefined")]
    public static partial int IsUndefined(BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_is_null")]
    public static partial int IsNull(BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_is_bool")]
    public static partial int IsBool(BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_is_number")]
    public static partial int IsNumber(BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_is_string")]
    public static partial int IsString(BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_is_object")]
    public static partial int IsObject(BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_is_callable")]
    public static partial int IsCallable(BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_to_bool")]
    public static partial int ToBool(BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_to_number")]
    public static partial double ToNumber(nint context, BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_to_int32")]
    public static partial int ToInt32(BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_to_utf8")]
    public static partial nint ToUtf8(nint context, BunValue value, out nuint length);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_set")]
    private static partial int SetCore(nint context, BunValue @object, byte* key, nuint keyLength, BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_get")]
    private static partial BunValue GetCore(nint context, BunValue @object, byte* key, nuint keyLength);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_set_index")]
    public static partial int SetIndex(nint context, BunValue @object, uint index, BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_get_index")]
    public static partial BunValue GetIndex(nint context, BunValue @object, uint index);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_define_getter")]
    private static partial int DefineGetterCore(nint context, BunValue @object, byte* key, nuint keyLength, nint getter, nint userdata, int dontEnum, int dontDelete);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_define_setter")]
    private static partial int DefineSetterCore(nint context, BunValue @object, byte* key, nuint keyLength, nint setter, nint userdata, int dontEnum, int dontDelete);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_define_accessor")]
    private static partial int DefineAccessorCore(nint context, BunValue @object, byte* key, nuint keyLength, nint getter, nint setter, nint userdata, int readOnly, int dontEnum, int dontDelete);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_define_finalizer")]
    public static partial int DefineFinalizer(nint context, BunValue @object, nint finalizer, nint userdata);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_set_prototype")]
    public static partial int SetPrototype(nint context, BunValue @object, BunValue prototype);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_set_opaque")]
    public static partial void SetOpaque(nint context, BunValue @object, nint opaquePtr);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_get_opaque")]
    public static partial nint GetOpaque(nint context, BunValue @object);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_call")]
    public static partial BunValue Call(nint context, BunValue function, BunValue thisValue, int argc, BunValue* argv);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_last_error")]
    public static partial nint LastError(nint context);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_call_async")]
    public static partial int CallAsync(nint context, BunValue function, BunValue thisValue, int argc, BunValue* argv);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_protect")]
    public static partial void Protect(nint context, BunValue value);

    [LibraryImport(BunNativeLibraryResolver.LibraryName, EntryPoint = "bun_unprotect")]
    public static partial void Unprotect(nint context, BunValue value);
}