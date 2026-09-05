using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Reports the worker unhealthy when its heartbeat has gone stale. A worker process that is running but
/// no longer beating is worse than one that has exited, because nothing restarts it; this check is what
/// turns that state into a restart.
/// </summary>
/// <param name="monitor">The heartbeat monitor.</param>
/// <param name="clock">The clock.</param>
public sealed class HeartbeatHealthCheck(IHeartbeatMonitor monitor, IClock clock) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var last = monitor.LastBeat;
        if (last is null)
        {
            return Task.FromResult(HealthCheckResult.Degraded("The worker has not completed its first heartbeat."));
        }

        var age = clock.UtcNow - last.Value;
        return Task.FromResult(age <= monitor.StaleAfter
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The worker heartbeat is stale."));
    }
}
