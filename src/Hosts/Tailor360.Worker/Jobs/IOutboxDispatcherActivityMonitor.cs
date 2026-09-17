namespace Tailor360.Worker.Jobs;

/// <summary>
/// Exposes whether <see cref="OutboxDispatcherService"/>'s own loop is still iterating, for
/// <see cref="OutboxDispatcherLivenessHealthCheck"/> to read — the same shape
/// <c>IHeartbeatMonitor</c> uses for the separate heartbeat loop. Two independent loops each answering for
/// their own liveness catch a partial deadlock that a single shared "the process is up" signal could not:
/// a stuck dispatcher does not stop the heartbeat's own <c>PeriodicTimer</c> from still ticking.
/// </summary>
public interface IOutboxDispatcherActivityMonitor
{
    /// <summary>When the dispatcher last began an iteration of its loop, before that iteration's own work — <see langword="null"/> before the first one.</summary>
    DateTimeOffset? LastIterationStartedAt { get; }

    /// <summary>How long with no new iteration is treated as the loop having stopped rather than merely being busy.</summary>
    TimeSpan StaleAfter { get; }
}
