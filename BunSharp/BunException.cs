using System;

namespace BunSharp;

public sealed class BunException : Exception
{
    public BunException(string message)
        : base(message)
    {
    }

    public BunException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}