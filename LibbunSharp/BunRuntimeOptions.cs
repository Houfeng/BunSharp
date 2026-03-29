namespace LibbunSharp;

public enum BunDebuggerMode
{
    Off = 0,
    Attach = 1,
    Wait = 2,
    Break = 3,
}

public sealed class BunRuntimeOptions
{
    public string? Cwd { get; init; }

    public BunDebuggerMode DebuggerMode { get; init; } = BunDebuggerMode.Off;

    public string? DebuggerListenUrl { get; init; }
}