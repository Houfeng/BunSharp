using System.Runtime.InteropServices;
using BunSharp.Interop;

namespace BunSharp;

/// <summary>
/// Retains a live JavaScript ArrayBuffer reference and exposes its shared byte
/// backing store directly.
/// Use this when the API needs the raw buffer object itself rather than a typed
/// view over that buffer. For typed binary views such as Uint8Array, prefer
/// <see cref="JSBufferRef"/> or <see cref="JSTypedArrayRef"/> depending on
/// whether the element kind should be constrained to bytes.
/// Call <see cref="Dispose"/> explicitly when the retained buffer is no longer
/// needed. Runtime teardown is only a fallback release path.
/// </summary>
public sealed class JSArrayBufferRef : IDisposable
{
    private readonly JSObjectRef _objectRef;

    public JSArrayBufferRef(BunContext context, BunValue value)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.TryGetArrayBuffer(value, out _))
        {
            throw new ArgumentException("Shared ArrayBuffer references require an ArrayBuffer value.", nameof(value));
        }

        _objectRef = new JSObjectRef(context, value);
    }

    public BunContext Context => _objectRef.Context;

    public BunValue Value => _objectRef.Value;

    public nint Data => GetInfo().Data;

    public nuint ByteLength => GetInfo().ByteLength;

    public BunArrayBufferInfo GetInfo()
    {
        if (!Context.TryGetArrayBuffer(Value, out var info))
        {
            throw new InvalidOperationException("The protected JS value is no longer an ArrayBuffer.");
        }

        return info;
    }

    public byte[] ToArray()
    {
        var info = GetInfo();
        var byteLength = checked((int)info.ByteLength);
        if (byteLength == 0)
        {
            return Array.Empty<byte>();
        }

        var result = GC.AllocateUninitializedArray<byte>(byteLength);
        Marshal.Copy(info.Data, result, 0, byteLength);
        return result;
    }

    public void Dispose()
    {
        _objectRef.Dispose();
        GC.SuppressFinalize(this);
    }
}