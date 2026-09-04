using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Platform.Persistence.Health;

/// <summary>
/// Reports that the database is reachable. This is the only check tagged for readiness: without the
/// database no request can be answered correctly, whereas every other dependency degrades a feature
/// rather than the instance.
/// </summary>
/// <param name="database">The platform context.</param>
public sealed class DatabaseHealthCheck(PlatformDbContext database) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database did not accept a connection.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception message is deliberately dropped: a connection failure can name the host and
            // the user, and this endpoint answers without a session.
            return HealthCheckResult.Unhealthy("The database did not accept a connection.");
        }
    }
}

/// <summary>
/// Reports whether the schema matches this build. Tagged for startup rather than readiness so that a
/// build with unapplied migrations is held back before it serves anything, instead of being restarted
/// in a loop by an orchestrator.
/// </summary>
/// <param name="runner">The migration runner.</param>
public sealed class MigrationStateHealthCheck(MigrationRunner runner) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var state = await runner.GetStateAsync(cancellationToken);

        if (!state.CanServe)
        {
            var schemas = string.Join(", ", state.PendingBySchema.Keys);
            return HealthCheckResult.Unhealthy($"Migrations are outstanding for: {schemas}.");
        }

        // A database ahead of this build is the expected state during a rollback, so it is reported and
        // not treated as a failure.
        return state.DatabaseIsAhead
            ? HealthCheckResult.Degraded("The database carries migrations this build does not know.")
            : HealthCheckResult.Healthy();
    }
}

/// <summary>
/// Reports the outbox backlog. A growing backlog means consumers have not heard about work that has
/// already happened, which stays invisible in the user interface until someone asks why a message never
/// arrived.
/// </summary>
/// <param name="database">The platform context.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Outbox configuration.</param>
public sealed class OutboxBacklogHealthCheck(
    PlatformDbContext database,
    IClock clock,
    IOptions<OutboxOptions> options)
    : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var deadLettered = await database.OutboxMessages
            .CountAsync(m => m.DeadLetteredAt != null, cancellationToken);

        if (deadLettered > 0)
        {
            return HealthCheckResult.Degraded("There are dead-lettered outbox messages awaiting replay.");
        }

        var oldest = await database.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.DeadLetteredAt == null)
            .OrderBy(m => m.OccurredAt)
            .Select(m => (DateTimeOffset?)m.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (oldest is null)
        {
            return HealthCheckResult.Healthy();
        }

        return clock.UtcNow - oldest.Value > options.Value.BacklogWarningAge
            ? HealthCheckResult.Degraded("The oldest undelivered outbox message is older than the agreed bound.")
            : HealthCheckResult.Healthy();
    }
}
