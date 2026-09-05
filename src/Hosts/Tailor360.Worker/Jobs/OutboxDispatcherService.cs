using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Persistence.Outbox;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Runs the outbox dispatcher in a loop. When a cycle delivered a full batch the next cycle starts
/// immediately, because a backlog is exactly when waiting is wrong; when a cycle found nothing the loop
/// waits for the idle interval so an empty outbox does not become a constant query.
/// </summary>
/// <param name="dispatcher">The dispatcher.</param>
/// <param name="options">Outbox configuration.</param>
/// <param name="workerOptions">Worker configuration, which supplies the instance name used as the lease owner.</param>
/// <param name="logger">Logger.</param>
public sealed class OutboxDispatcherService(
    OutboxDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    IOptions<WorkerOptions> workerOptions,
    ILogger<OutboxDispatcherService> logger)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var owner = workerOptions.Value.InstanceName;
        WorkerLog.OutboxDispatcherStarting(logger, owner);

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;
            try
            {
                processed = await dispatcher.RunCycleAsync(owner, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A failure here is the dispatcher itself failing, not a message failing; the loop must
                // survive it or one bad cycle stops all delivery until someone notices.
                WorkerLog.OutboxCycleFailed(logger, exception);
                processed = 0;
            }

            if (processed < options.Value.BatchSize)
            {
                try
                {
                    await Task.Delay(options.Value.IdlePollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
