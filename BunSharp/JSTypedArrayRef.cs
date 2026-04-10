using System.Runtime.InteropServices;
using BunSharp.Interop;

namespace BunSharp;

public sealed class JSTypedArrayRef : IDisposable
{
    private readonly JSObjectRef _objectRef;

    public JSTypedArrayRef(BunContext context, BunValue value)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.TryGetTypedArray(value, out _))
        {
            throw new ArgumentException("Shared typed array references require a TypedArray value.", nameof(value));
        }

        _objectRef = new JSObjectRef(context, value);
    }

    public BunContext Context => _objectRef.Context;

    public BunValue Value => _objectRef.Value;

    public nint Data => GetInfo().Data;

    public nuint ByteOffset => GetInfo().ByteOffset;

    public nuint ByteLength => GetInfo().ByteLength;

    public nuint ElementCount => GetInfo().ElementCount;

    public BunTypedArrayKind Kind => GetInfo().Kind;

    public BunTypedArrayInfo GetInfo()
    {
        if (!Context.TryGetTypedArray(Value, out var info))
        {
            throw new InvalidOperationException("The protected JS value is no longer a TypedArray.");
        }

        return info;
    }

    public byte[] ToByteArray()
    {
        var info = GetInfo();
        var byteLength = checked((int)info.ByteLength);
        if (byteLength == 0)
        {
            return Array.Empty<byte>();
        }

        var result = GC.AllocateUninitializedArray<byte>(byteLength);
        Marshal.Copy(IntPtr.Add(info.Data, checked((int)info.ByteOffset)), result, 0, byteLength);
        return result;
    }

    public void Dispose()
    {
        _objectRef.Dispose();
        GC.SuppressFinalize(this);
    }
}