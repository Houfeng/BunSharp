using System.Runtime.InteropServices;
using BunSharp.Interop;

namespace BunSharp;

/// <summary>
/// Retains a live JavaScript TypedArray reference and exposes its native layout.
/// Use this when the API may receive different TypedArray kinds and the managed
/// side needs to inspect <see cref="Kind"/>, element count, or byte layout.
/// For byte-oriented APIs that should accept only Uint8Array or Buffer-like
/// values, prefer <see cref="JSBufferRef"/>.
/// Call <see cref="Dispose"/> explicitly when the retained typed array is no
/// longer needed. Runtime teardown is only a fallback release path.
/// </summary>
public sealed class JSTypedArrayRef : IDisposable
{
    private readonly JSObjectRef _objectRef;
    private readonly nuint _byteOffset;
    private readonly BunTypedArrayKind _kind;

    public JSTypedArrayRef(BunContext context, BunValue value)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.TryGetTypedArray(value, out var info))
        {
            throw new ArgumentException("Shared typed array references require a TypedArray value.", nameof(value));
        }

        _byteOffset = info.ByteOffset;
        _kind = info.Kind;
        _objectRef = new JSObjectRef(context, value);
    }

    public BunContext Context => _objectRef.Context;

    public BunValue Value => _objectRef.Value;

    public nint Data => GetInfo().Data;

    public nuint ByteOffset => _byteOffset;

    public nuint ByteLength => GetInfo().ByteLength;

    public nuint ElementCount => GetInfo().ElementCount;

    public BunTypedArrayKind Kind => _kind;

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