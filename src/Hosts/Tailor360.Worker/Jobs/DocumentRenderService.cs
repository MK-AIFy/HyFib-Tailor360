using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Platform.Abstractions.Scheduling;
using Tailor360.Platform.Security.Background;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Renders the documents posted since the last pass (#155): every pending artefact is rendered, stored and
/// completed by <see cref="DocumentArtifactHandler"/>, outside any transaction, under the job lease so that
/// two worker instances never render one document twice. A rendering that fails is retried on the next
/// pass and failed for good after the bounded attempts — an operational alert, never a reason to re-post
/// (<c>INV-INV-08</c>).
/// </summary>
[WorkerJob(JobName, WorkerBranchScope.Organisation, BillingPermissions.CreateInvoice)]
public sealed partial class DocumentRenderService(
    IWorkerScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    IOptions<DocumentOptions> documentOptions,
    ILogger<DocumentRenderService> logger)
    : BackgroundService
{
    /// <summary>The job's name, as the lease and the audit know it.</summary>
    public const string JobName = "billing.render_documents";

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(documentOptions.Value.RenderInterval);
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
            using var scope = scopeFactory.CreateSystemScope(typeof(DocumentRenderService));
            var leases = scope.Services.GetRequiredService<IJobLease>();
            var owner = options.Value.InstanceName;
            if (!await leases.TryAcquireAsync(JobName, owner, LeaseDuration, cancellationToken))
            {
                return;
            }

            try
            {
                var handler = scope.Services.GetRequiredService<DocumentArtifactHandler>();
                // A pass stops a minute short of the lease, so a slow store never outlives the lock that keeps
                // two workers off one document.
                var rendered = await handler.RenderPendingAsync(LeaseDuration - TimeSpan.FromMinutes(1), cancellationToken);
                if (rendered > 0)
                {
                    LogRendered(logger, rendered);
                }
            }
            finally
            {
                await leases.ReleaseAsync(JobName, owner, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The loop survives a failed pass: the pending rows are still pending and the next pass finds them.
            LogPassFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rendered {Count} document(s).")]
    private static partial void LogRendered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The document rendering pass failed; the pending documents stay pending.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
