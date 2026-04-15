using System.Runtime.InteropServices;

namespace BunSharp.Interop;

// Keep the default int backing type because this enum is embedded in
// BunInitializeOptions, which must stay layout-compatible with the C struct in
// bun_embed.h.
internal enum BunNativeDebuggerMode
{
    Off = 0,
    Attach = 1,
    Wait = 2,
    Break = 3,
}

// Keep the default int backing type because this enum is used in native-facing
// P/Invoke signatures and sequential structs, so its width must match the C
// enum layout in bun_embed.h.
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
public delegate void BunEventCallbackFunction(nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate BunValue BunGetterFunction(nint ctx, BunValue thisValue, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BunSetterFunction(nint ctx, BunValue thisValue, BunValue value, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BunFinalizerFunction(nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate BunValue BunClassMethodFunction(nint ctx, BunValue thisValue, nint nativePtr, int argc, BunValue* argv, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate BunValue BunClassGetterFunction(nint ctx, BunValue thisValue, nint nativePtr, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BunClassSetterFunction(nint ctx, BunValue thisValue, nint nativePtr, BunValue value, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate BunValue BunClassConstructorFunction(nint ctx, nint klass, int argc, BunValue* argv, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate BunValue BunClassStaticMethodFunction(nint ctx, BunValue thisValue, nint userdata, int argc, BunValue* argv);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate BunValue BunClassStaticGetterFunction(nint ctx, BunValue thisValue, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BunClassStaticSetterFunction(nint ctx, BunValue thisValue, BunValue value, nint userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void BunClassFinalizerFunction(nint nativePtr, nint userdata);

[StructLayout(LayoutKind.Sequential)]
internal struct BunInitializeOptions
{
    public nint Cwd;
    public BunNativeDebuggerMode DebuggerMode;
    public nint DebuggerListenUrl;
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
public struct BunClassStaticMethodDescriptor
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
public struct BunClassStaticPropertyDescriptor
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
    public nint Constructor;
    public nint ConstructorUserData;
    public int ConstructorArgCount;
    public BunClassStaticPropertyDescriptor* StaticProperties;
    public nuint StaticPropertyCount;
    public BunClassStaticMethodDescriptor* StaticMethods;
    public nuint StaticMethodCount;
}