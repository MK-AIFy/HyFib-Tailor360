using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Application.Options;
using Tailor360.Platform.Abstractions.Scheduling;
using Tailor360.Platform.Security.Background;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Destroys the copies of customers' personal data held by expired subject-access exports.
/// </summary>
/// <remarks>
/// <para>
/// The expiry on an export is enforced twice, and this is the half that matters. The download endpoint
/// refuses an expired export, which stops anybody reading it — but refusing to serve a copy is not the
/// same as not holding one, and <c>docs/nfr/data-classification.md</c> section 9 says an export
/// <em>expires</em>, not that it stops being served. Without this job the rows would sit in the
/// database holding somebody's name, telephone number, address and consent history for as long as the
/// installation lives, and the only thing standing between that and a breach would be an endpoint
/// check.
/// </para>
/// <para>
/// It empties the document and keeps everything else. That an export was taken, by whom and why is the
/// shop's record of how it answered a request and is evidence in its own right; the copy of the data is
/// the part with a lifetime.
/// </para>
/// <para>
/// Under a lease, so that only one worker instance does the work when several are running, and bounded
/// by <see cref="CustomerExportOptions.PurgeBatchSize"/>, so a long outage drains over several passes
/// rather than one transaction over the whole backlog.
/// </para>
/// <para>
/// The lease is taken through <see cref="IJobLease"/> rather than the concrete service, so this shell
/// can be executed by a test. It matters more here than on a job that only moves rows: this one
/// destroys copies of people's personal data, and "the loop kept running after a failed pass" and "the
/// lease was released even when the work threw" are promises worth asserting rather than reading.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Opens the job's scope per run.</param>
/// <param name="options">Worker configuration, which supplies the instance name used as the lease owner.</param>
/// <param name="exportOptions">How often to run and how much to take.</param>
/// <param name="logger">Logger.</param>
[WorkerJob(JobName, WorkerBranchScope.None)]
public sealed class CustomerExportPurgeService(
    IWorkerScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    IOptions<CustomerExportOptions> exportOptions,
    ILogger<CustomerExportPurgeService> logger)
    : BackgroundService
{
    /// <summary>The declared job name, which is also the lease name this job takes.</summary>
    public const string JobName = "customers.export_purge";

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Once at start-up. A deployment is the moment an installation that was stopped over a weekend
        // comes back, and the exports that expired while it was down are exactly the ones that have
        // been sitting there longest.
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(exportOptions.Value.PurgeInterval);

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
            using var scope = scopeFactory.CreateSystemScope(typeof(CustomerExportPurgeService));
            var leases = scope.Services.GetRequiredService<IJobLease>();
            var owner = options.Value.InstanceName;

            if (!await leases.TryAcquireAsync(JobName, owner, LeaseDuration, cancellationToken))
            {
                return;
            }

            try
            {
                var handler = scope.Services.GetRequiredService<CustomerExportHandler>();
                var purged = await handler.PurgeExpiredAsync(cancellationToken);

                if (purged.IsFailure)
                {
                    WorkerLog.CustomerExportPurgeRefused(logger, purged.Error.Code);

                    return;
                }

                if (purged.Value > 0)
                {
                    WorkerLog.CustomerExportsPurged(logger, purged.Value);
                }
            }
            finally
            {
                await leases.ReleaseAsync(JobName, owner, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The loop must survive a failed run. A pass that fails leaves the copies in place and the
            // next pass finds them again, which is the right way round: the failure mode is a copy
            // living longer than it should and being reported, not a copy silently believed destroyed.
            WorkerLog.CustomerExportPurgeFailed(logger, exception);
        }
    }
}
