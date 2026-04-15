namespace BunSharp;

/// <summary>
/// Result of a non-blocking embed event-loop tick from <see cref="BunRuntime.RunPendingJobs"/>.
/// </summary>
// Keep the default int backing type because this enum crosses the native ABI
// as the return value of bun_run_pending_jobs(). C enums in bun_embed.h are
// int-sized here, so shrinking the managed type can break P/Invoke marshalling.
public enum BunPendingJobsResult
{
    /// <summary>Runtime is fully idle: no queued work and no active handles/timers.</summary>
    Idle = 0,
    /// <summary>More work can be processed immediately without waiting for a new wakeup.</summary>
    Spin = 1,
    /// <summary>Runtime is still active, but further progress requires a future wakeup.</summary>
    Wait = 2,
}
