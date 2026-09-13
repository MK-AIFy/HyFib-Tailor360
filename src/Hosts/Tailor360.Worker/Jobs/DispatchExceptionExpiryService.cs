using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Payments;
using Tailor360.Platform.Abstractions.Scheduling;
using Tailor360.Platform.Security.Background;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Marks a single-use dispatch exception expired once it has passed its expiry without ever being
/// consumed (#164, EX-15). Custody's dispatch scan (#37) is not built yet, so today an expired exception
/// is refused only if something were to try consuming it after the fact; this pass keeps the record
/// straight regardless, and gives the Owner dashboard an accurate answer once #45 reads it.
/// </summary>
/// <remarks>
/// Under a lease, so that only one worker instance runs a pass when several are running, and bounded by
/// a fixed batch size, so a long outage drains over several passes rather than one transaction over the
/// whole backlog. An exception past its expiry is harmless left alone — <see cref="DispatchException"/>
/// treats "past its expiry" as expired whether or not this pass has run yet — so a missed or delayed
/// pass never lets a stale exception be consumed; it only leaves the record saying "approved" a little
/// longer than the truth.
/// </remarks>
/// <param name="scopeFactory">Opens the job's scope per run.</param>
/// <param name="options">Worker configuration, which supplies the instance name used as the lease owner.</param>
/// <param name="logger">Logger.</param>
[WorkerJob(JobName, WorkerBranchScope.None)]
public sealed class DispatchExceptionExpiryService(
    IWorkerScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<DispatchExceptionExpiryService> logger)
    : BackgroundService
{
    /// <summary>The declared job name, which is also the lease name this job takes.</summary>
    public const string JobName = "billing.dispatch_exception_expiry";

    private const int BatchSize = 200;

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(RunInterval);

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
            using var scope = scopeFactory.CreateSystemScope(typeof(DispatchExceptionExpiryService));
            var leases = scope.Services.GetRequiredService<IJobLease>();
            var owner = options.Value.InstanceName;

            if (!await leases.TryAcquireAsync(JobName, owner, LeaseDuration, cancellationToken))
            {
                return;
            }

            try
            {
                var handler = scope.Services.GetRequiredService<DispatchExceptionHandler>();
                var expired = await handler.ExpireDueAsync(BatchSize, cancellationToken);

                if (expired.IsFailure)
                {
                    WorkerLog.DispatchExceptionExpiryRefused(logger, expired.Error.Code);

                    return;
                }

                if (expired.Value > 0)
                {
                    WorkerLog.DispatchExceptionsExpired(logger, expired.Value);
                }
            }
            finally
            {
                await leases.ReleaseAsync(JobName, owner, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The loop must survive a failed pass. An exception left un-marked past its expiry is still
            // refused at consumption on its own account, so the failure mode here is a stale record, not
            // a stale grant.
            WorkerLog.DispatchExceptionExpiryFailed(logger, exception);
        }
    }
}
