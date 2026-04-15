namespace BunSharp;

public enum BunDebuggerMode: byte
{
    Off = 0,
    Attach = 1,
    Wait = 2,
    Break = 3,
}

public sealed class BunRuntimeOptions
{
    /// <summary>
    /// Working directory passed to libbun. Use <see langword="null"/> to keep the current process directory.
    /// </summary>
    public string? Cwd { get; init; }


    /// <summary>
    /// Controls inspector startup behavior.
    /// </summary>
    public BunDebuggerMode DebuggerMode { get; init; } = BunDebuggerMode.Off;

    /// <summary>
    /// Optional inspector listen URL or path.
    /// Examples: 6499, 127.0.0.1:6499, ws://0.0.0.0:6499/debug, unix:///tmp/bun-debug.sock.
    /// </summary>
    public string? DebuggerListenUrl { get; init; }
}