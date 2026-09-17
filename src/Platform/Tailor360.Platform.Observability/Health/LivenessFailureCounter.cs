namespace Tailor360.Platform.Observability.Health;

/// <summary>
/// The liveness watchdog's counting rule, isolated from <see cref="LivenessWatchdogService"/>'s own
/// health-check plumbing so it is testable without a hosted service or a real
/// <c>HealthCheckService</c>: <c>docs/architecture/container.md</c>'s "three consecutive liveness
/// failures exit with code 70" — consecutive, not cumulative, so one success anywhere in the run resets
/// the count to zero.
/// </summary>
/// <param name="consecutiveFailuresBeforeExit">How many failures in a row, with no success between them, before the watchdog should act.</param>
public sealed class LivenessFailureCounter(int consecutiveFailuresBeforeExit)
{
    private int _consecutiveFailures;

    /// <summary>The number of failures observed since the last success, or since construction.</summary>
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>Records a failed check.</summary>
    /// <returns><see langword="true"/> exactly on the observation that reaches the configured threshold — the caller should act on this result once, not on every failure after it.</returns>
    public bool RecordFailure() => Interlocked.Increment(ref _consecutiveFailures) >= consecutiveFailuresBeforeExit;

    /// <summary>Records a successful check, resetting the run.</summary>
    public void RecordSuccess() => Interlocked.Exchange(ref _consecutiveFailures, 0);
}
