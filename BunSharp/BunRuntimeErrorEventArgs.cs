using System;

namespace BunSharp;

public enum BunRuntimeDiagnosticSource
{
    EventCallback,
    Cleanup,
    Finalizer,
    ClassFinalizer,
    PersistentClassFinalizer,
    ObjectFinalizer,
}

public sealed class BunRuntimeErrorEventArgs : EventArgs
{
    internal BunRuntimeErrorEventArgs(
        BunRuntimeDiagnosticSource source,
        Exception exception,
        bool isBackgroundThread,
        bool isDuringRuntimeDisposal,
        int threadId,
        DateTimeOffset timestamp)
    {
        Source = source;
        Exception = exception;
        IsBackgroundThread = isBackgroundThread;
        IsDuringRuntimeDisposal = isDuringRuntimeDisposal;
        ThreadId = threadId;
        Timestamp = timestamp;
    }

    public BunRuntimeDiagnosticSource Source { get; }

    public Exception Exception { get; }

    public bool IsBackgroundThread { get; }

    public bool IsDuringRuntimeDisposal { get; }

    public int ThreadId { get; }

    public DateTimeOffset Timestamp { get; }
}