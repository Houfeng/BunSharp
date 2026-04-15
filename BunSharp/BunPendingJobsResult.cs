namespace BunSharp;

/// <summary>
/// Result of a non-blocking embed event-loop tick from <see cref="BunRuntime.RunPendingJobs"/>.
/// </summary>
public enum BunPendingJobsResult : byte
{
    /// <summary>Runtime is fully idle: no queued work and no active handles/timers.</summary>
    Idle = 0,
    /// <summary>More work can be processed immediately without waiting for a new wakeup.</summary>
    Spin = 1,
    /// <summary>Runtime is still active, but further progress requires a future wakeup.</summary>
    Wait = 2,
}
