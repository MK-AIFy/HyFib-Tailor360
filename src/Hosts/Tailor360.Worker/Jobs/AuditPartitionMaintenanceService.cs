using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Scheduling;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Keeps audit partitions provisioned ahead of time. Without this the trail would rely on someone
/// running a command every few months, and the consequence of forgetting is not a warning: audit
/// entries are written in the same transaction as the change they describe, so entries landing in the
/// default partition accumulate silently until a migration refuses to create that month's partition.
///
/// The job runs at start-up and daily thereafter, under a lease so that only one worker instance does
/// the work when several are running.
/// </summary>
/// <param name="scopeFactory">Creates a scope per run.</param>
/// <param name="options">Worker configuration, which supplies the instance name used as the lease owner.</param>
/// <param name="logger">Logger.</param>
public sealed class AuditPartitionMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<AuditPartitionMaintenanceService> logger)
    : BackgroundService
{
    /// <summary>The lease name this job takes.</summary>
    public const string JobName = "platform.audit_partition_maintenance";

    /// <summary>How many months of partitions are kept ahead of the current one.</summary>
    public const int MonthsAhead = 3;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once at start-up: a deployment is the moment a long-stopped installation comes back, and
        // waiting a day to provision partitions would leave that window unprotected.
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var leases = scope.ServiceProvider.GetRequiredService<JobLeaseService>();
            var owner = options.Value.InstanceName;

            if (!await leases.TryAcquireAsync(JobName, owner, LeaseDuration, cancellationToken))
            {
                return;
            }

            try
            {
                var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

                for (var month = 0; month <= MonthsAhead; month++)
                {
                    await context.Database.ExecuteSqlAsync(
                        $"SELECT platform.ensure_audit_partition(now() + ({month} * interval '1 month'))",
                        cancellationToken);
                }

                WorkerLog.AuditPartitionsProvisioned(logger, MonthsAhead);
            }
            finally
            {
                await leases.ReleaseAsync(JobName, owner, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The loop must survive a failed run; the health check reports the consequence separately.
            WorkerLog.AuditPartitionMaintenanceFailed(logger, exception);
        }
    }
}
