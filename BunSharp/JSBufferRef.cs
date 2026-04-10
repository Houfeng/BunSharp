using BunSharp.Interop;

namespace BunSharp;

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