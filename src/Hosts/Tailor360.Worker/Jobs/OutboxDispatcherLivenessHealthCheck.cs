using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Tagged <c>HealthCheckTags.Live</c>: asserts the outbox dispatcher's loop is still iterating, not that
/// its dependency is reachable. It never calls the database itself — that distinction is what
/// <c>docs/architecture/failure-modes.md</c> requires of every <c>Live</c>-tagged check, because a check
/// that dialled out would turn a shared-dependency outage into every host restarting at once.
/// </summary>
/// <param name="monitor">The dispatcher's own activity, read rather than probed.</param>
/// <param name="clock">The clock the staleness window is measured against.</param>
public sealed class OutboxDispatcherLivenessHealthCheck(IOutboxDispatcherActivityMonitor monitor, IClock clock) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var lastStarted = monitor.LastIterationStartedAt;
        if (lastStarted is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("The outbox dispatcher has not started its first cycle."));
        }

        var age = clock.UtcNow - lastStarted.Value;
        var result = age > monitor.StaleAfter
            ? HealthCheckResult.Unhealthy("The outbox dispatcher has not started a new cycle within its expected interval.")
            : HealthCheckResult.Healthy();

        return Task.FromResult(result);
    }
}
