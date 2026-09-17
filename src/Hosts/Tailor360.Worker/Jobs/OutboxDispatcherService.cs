using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Outbox;
using Tailor360.Platform.Security.Background;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Runs the outbox dispatcher in a loop. When a cycle delivered a full batch the next cycle starts
/// immediately, because a backlog is exactly when waiting is wrong; when a cycle found nothing the loop
/// waits for the idle interval so an empty outbox does not become a constant query.
/// </summary>
/// <remarks>
/// The declaration below is the loop's, not a message handler's. Delivery itself is not a user action:
/// it carries out an effect a command already authorised and committed, so the loop holds no permission
/// and the principal it would run as holds nothing. A handler that acts for the person whose command
/// produced the message opens its own scope from that message's stored requester, which is what
/// <see cref="IWorkerScopeFactory.CreateScopeForAsync"/> exists for; there are no handlers yet, and the
/// dispatcher's own scopes stay where they are, in the persistence layer that owns the claim.
/// </remarks>
/// <param name="dispatcher">The dispatcher.</param>
/// <param name="options">Outbox configuration.</param>
/// <param name="workerOptions">Worker configuration, which supplies the instance name used as the lease owner.</param>
/// <param name="clock">The clock the last-iteration timestamp is measured against.</param>
/// <param name="logger">Logger.</param>
[WorkerJob(JobName, WorkerBranchScope.Organisation)]
public sealed class OutboxDispatcherService(
    OutboxDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    IOptions<WorkerOptions> workerOptions,
    IClock clock,
    ILogger<OutboxDispatcherService> logger)
    : BackgroundService, IOutboxDispatcherActivityMonitor
{
    /// <summary>The declared job name.</summary>
    public const string JobName = "platform.outbox_dispatcher";

    private long _lastIterationStartedTicks;

    /// <inheritdoc />
    public DateTimeOffset? LastIterationStartedAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastIterationStartedTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A generous multiple of the poll interval, not of how long one cycle might legitimately take: the
    /// stamp below is written at the top of every iteration, before <see cref="OutboxDispatcher.RunCycleAsync"/>
    /// is even called, and the loop's own <c>catch</c> means a failing cycle still comes back around after
    /// at most <see cref="OutboxOptions.IdlePollInterval"/>. Staleness beyond this floor means the loop
    /// itself stopped iterating — a deadlock or thread-pool starvation — not that one cycle is slow or
    /// retrying, which is exactly the distinction <c>docs/architecture/resilience-policies.md</c> draws
    /// between "the dependency is slow" and "the process is stuck".
    /// </remarks>
    public TimeSpan StaleAfter => TimeSpan.FromSeconds(Math.Max(options.Value.IdlePollInterval.TotalSeconds * 15, 60));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var owner = workerOptions.Value.InstanceName;
        WorkerLog.OutboxDispatcherStarting(logger, owner);

        while (!stoppingToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _lastIterationStartedTicks, clock.UtcNow.UtcTicks);

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
