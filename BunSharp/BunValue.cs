using System;
using System.Runtime.InteropServices;

namespace BunSharp;

[StructLayout(LayoutKind.Sequential)]
public readonly struct BunValue : IEquatable<BunValue>
{
    public BunValue(ulong rawValue)
    {
        RawValue = rawValue;
    }

    public ulong RawValue { get; }

    public static BunValue Undefined => new(0xAUL);

    public static BunValue Null => new(0x2UL);

    public static BunValue True => new(0x7UL);

    public static BunValue False => new(0x6UL);

    public static BunValue Exception => new(0UL);

    public bool IsException => RawValue == Exception.RawValue;

    public bool Equals(BunValue other) => RawValue == other.RawValue;

    public override bool Equals(object? obj) => obj is BunValue other && Equals(other);

    public override int GetHashCode() => RawValue.GetHashCode();

    public override string ToString() => $"0x{RawValue:X}";

    public static bool operator ==(BunValue left, BunValue right) => left.Equals(right);

    public static bool operator !=(BunValue left, BunValue right) => !left.Equals(right);

    public static implicit operator BunValue(ulong rawValue) => new(rawValue);

    public static implicit operator ulong(BunValue value) => value.RawValue;
}