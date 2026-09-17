using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tailor360.Modules.Integration.Infrastructure.Storage;

/// <summary>
/// Reports the object-storage circuit breaker's state on <c>/health/detail</c>, tagged
/// <c>HealthCheckTags.NonEssential</c> — <c>docs/architecture/failure-modes.md</c> section 5's "Storage
/// reports Degraded on <c>/health/detail</c>". Never reports <see cref="HealthCheckResult.Unhealthy"/> and
/// never carries the <c>ready</c> or <c>live</c> tag: object storage is a feature, not the instance
/// (principle 2 of the same document), so its own outage can never remove the host from rotation.
/// </summary>
/// <param name="storage">The resilient decorator, read for its breaker's own state rather than probed with
/// a fresh call — a health check that itself called into a hung dependency would need its own timeout, and
/// the breaker already answers the question without one.</param>
public sealed class ObjectStorageHealthCheck(ResilientObjectStorage storage) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The description names no endpoint, no bucket and no exception — the same rule
        // DatabaseHealthCheck follows, because any of those could disclose the deployment's own topology
        // to a caller who holds nothing but admin.health.read.
        //
        // Half-open counts as Degraded too, not just Open: it means the break has elapsed but no trial
        // call has yet confirmed the dependency actually recovered, and reporting Healthy on a guess
        // would be exactly the kind of premature all-clear this endpoint exists to avoid.
        var result = storage.Breaker.State switch
        {
            ObjectStorageCircuitState.Open => HealthCheckResult.Degraded("Object storage is not answering; the circuit breaker is open."),
            ObjectStorageCircuitState.HalfOpen => HealthCheckResult.Degraded("Object storage is not yet confirmed recovered; the circuit breaker is half-open."),
            _ => HealthCheckResult.Healthy(),
        };

        return Task.FromResult(result);
    }
}
