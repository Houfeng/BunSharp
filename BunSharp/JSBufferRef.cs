using System;
using BunSharp.Interop;

namespace BunSharp;

/// <summary>
/// Retains a live byte-oriented JavaScript view and restricts it to
/// Uint8Array-compatible values, including Buffer-like subclasses.
/// Use this for binary payloads where callers should not need to reason about
/// arbitrary TypedArray kinds. For non-byte TypedArrays or when the element kind
/// matters, prefer <see cref="JSTypedArrayRef"/>.
/// Call <see cref="Dispose"/> explicitly when the retained byte view is no
/// longer needed. If the wrapper graph is abandoned, the wrapped
/// <see cref="JSTypedArrayRef"/> and <see cref="JSObjectRef"/> provide the same
/// finalizer-backed fallback release path; explicit disposal is still
/// recommended.
/// </summary>
public sealed class JSBufferRef : IDisposable
{
    private readonly JSTypedArrayRef _typedArrayRef;

    public JSBufferRef(BunContext context, BunValue value)
    {
        ArgumentNullException.ThrowIfNull(context);

        _typedArrayRef = new JSTypedArrayRef(context, value);
        if (_typedArrayRef.Kind != BunTypedArrayKind.Uint8Array)
        {
            _typedArrayRef.Dispose();
            throw new ArgumentException("Buffer references require a Uint8Array-compatible value.", nameof(value));
        }
    }

    public BunContext Context => _typedArrayRef.Context;

    public BunValue Value => _typedArrayRef.Value;

    public nint Data => _typedArrayRef.Data;

    public nuint ByteOffset => _typedArrayRef.ByteOffset;

    public nuint ByteLength => _typedArrayRef.ByteLength;

    public nuint ElementCount => _typedArrayRef.ElementCount;

    public byte[] ToArray() => _typedArrayRef.ToByteArray();

    public void Dispose()
    {
        _typedArrayRef.Dispose();
        GC.SuppressFinalize(this);
    }
}