using System.Runtime.InteropServices;

namespace LibbunSharp.Interop;

public enum BunTypedArrayKind
{
    Int8Array = 0,
    Uint8Array = 1,
    Uint8ClampedArray = 2,
    Int16Array = 3,
    Uint16Array = 4,
    Int32Array = 5,
    Uint32Array = 6,
    Float32Array = 7,
    Float64Array = 8,
    BigInt64Array = 9,
    BigUint64Array = 10,
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate BunValue BunHostFunction(nint ctx, int argc, BunValue* argv, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate BunValue BunGetterFunction(nint ctx, BunValue thisValue);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BunSetterFunction(nint ctx, BunValue thisValue, BunValue value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BunFinalizerFunction(nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate BunValue BunClassMethodFunction(nint ctx, BunValue thisValue, nint nativePtr, int argc, BunValue* argv, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate BunValue BunClassGetterFunction(nint ctx, BunValue thisValue, nint nativePtr, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BunClassSetterFunction(nint ctx, BunValue thisValue, nint nativePtr, BunValue value, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BunClassFinalizerFunction(nint nativePtr, nint userdata);

[StructLayout(LayoutKind.Sequential)]
public struct BunEvalResult
{
    public int Success;
    public nint Error;

    public readonly string? ErrorMessage => Error == 0 ? null : Marshal.PtrToStringUTF8(Error);
}

[StructLayout(LayoutKind.Sequential)]
public struct BunArrayBufferInfo
{
    public nint Data;
    public nuint ByteLength;
}

[StructLayout(LayoutKind.Sequential)]
public struct BunTypedArrayInfo
{
    public nint Data;
    public nuint ByteOffset;
    public nuint ByteLength;
    public nuint ElementCount;
    public BunTypedArrayKind Kind;
}

[StructLayout(LayoutKind.Sequential)]
public struct BunClassMethodDescriptor
{
    public nint Name;
    public nuint NameLength;
    public nint Callback;
    public nint UserData;
    public int ArgCount;
    public int DontEnum;
    public int DontDelete;
}

[StructLayout(LayoutKind.Sequential)]
public struct BunClassPropertyDescriptor
{
    public nint Name;
    public nuint NameLength;
    public nint Getter;
    public nint Setter;
    public nint UserData;
    public int ReadOnly;
    public int DontEnum;
    public int DontDelete;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct BunClassDescriptor
{
    public nint Name;
    public nuint NameLength;
    public BunClassPropertyDescriptor* Properties;
    public nuint PropertyCount;
    public BunClassMethodDescriptor* Methods;
    public nuint MethodCount;
}