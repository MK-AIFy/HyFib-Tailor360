using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Health;

namespace Tailor360.Platform.Observability.Health;

/// <summary>
/// Runs on both hosts. Polls <c>HealthCheckTags.Live</c> on an interval, with its own timeout, and after
/// three consecutive failures exits with code 70 so the container's <c>restart: unless-stopped</c> policy
/// recovers it — <c>docs/architecture/container.md</c> and <c>docs/architecture/failure-modes.md</c>
/// section 2. Compose does not restart a container it merely reports as <c>unhealthy</c>, which is why
/// this exists at all rather than relying on the orchestrator alone.
/// </summary>
/// <remarks>
/// <para>
/// **Why the watchdog cannot trust a check's last reported status.** Every host's own <c>"self"</c> check
/// is <c>() =&gt; HealthCheckResult.Healthy()</c> — a constant. A watchdog that only read that status could
/// never fire, so <see cref="ProbeAsync"/> treats its own timeout, and any exception the check throws, as
/// a failure in its own right. That is what actually catches thread-pool starvation and a deadlocked
/// process: the condition that leaves every check unable to even answer, not one that answers unhealthy.
/// </para>
/// <para>
/// **Why liveness performs no dependency call.** A host whose database is unreachable must not be
/// restarted by this: <c>HealthCheckTags.Live</c> is never registered against anything that calls out, so
/// a shared-dependency outage degrades every instance identically instead of turning into a restart loop
/// that helps nobody. See <c>docs/architecture/resilience-policies.md</c>.
/// </para>
/// </remarks>
/// <remarks>
/// Not sealed: <c>Tailor360.Worker</c> composes a one-line subclass, <c>WorkerLivenessWatchdogService</c>,
/// carrying <c>[WorkerJob]</c> — architecture rule ARCH-021 requires every hosted service the worker
/// composes to declare one, and that attribute lives in <c>Tailor360.Platform.Security</c>, which this
/// project cannot reference (the flat layering among <c>Platform.*</c> projects). The web host registers
/// this class directly; ARCH-021 does not reach it there.
/// </remarks>
/// <param name="healthCheckService">The already-registered health checks, filtered to the <c>Live</c> tag.</param>
/// <param name="options">Interval, timeout and failure-count settings.</param>
/// <param name="logger">Logger.</param>
public class LivenessWatchdogService(
    HealthCheckService healthCheckService,
    IOptions<LivenessWatchdogOptions> options,
    ILogger<LivenessWatchdogService> logger)
    : BackgroundService
{
    /// <summary>The exit code a failed watchdog uses, so a container log line and the orchestrator's own record agree on what happened.</summary>
    public const int LivenessExitCode = 70;

    private readonly LivenessFailureCounter _counter = new(options.Value.ConsecutiveFailuresBeforeExit);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ObservabilityLog.LivenessWatchdogStarting(logger, options.Value.Interval, options.Value.ConsecutiveFailuresBeforeExit);

        using var timer = new PeriodicTimer(options.Value.Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown was requested; a stopping host is expected to stop watching itself.
        }
    }

    private async Task CheckOnceAsync(CancellationToken stoppingToken)
    {
        if (await ProbeAsync(stoppingToken))
        {
            _counter.RecordSuccess();
            return;
        }

        var reachedThreshold = _counter.RecordFailure();
        ObservabilityLog.LivenessCheckFailed(logger, _counter.ConsecutiveFailures, options.Value.ConsecutiveFailuresBeforeExit);

        if (reachedThreshold)
        {
            ObservabilityLog.LivenessWatchdogExiting(logger, _counter.ConsecutiveFailures);
            Environment.Exit(LivenessExitCode);
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken stoppingToken)
    {
        using var timeoutSource = new CancellationTokenSource(options.Value.CheckTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutSource.Token);

        try
        {
            var report = await healthCheckService.CheckHealthAsync(
                registration => registration.Tags.Contains(HealthCheckTags.Live), linked.Token);
            return report.Status == HealthStatus.Healthy;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Either the watchdog's own timeout fired or the check threw outright. Both are treated as a
            // liveness failure, deliberately, per the remarks above.
            ObservabilityLog.LivenessProbeDidNotAnswer(logger, exception);
            return false;
        }
    }
}

/// <summary>Source-generated log messages for the liveness watchdog.</summary>
internal static partial class ObservabilityLog
{
    [LoggerMessage(
        EventId = 5201,
        Level = LogLevel.Information,
        Message = "Liveness watchdog starting: every {Interval}, exits after {Threshold} consecutive failures.")]
    public static partial void LivenessWatchdogStarting(ILogger logger, TimeSpan interval, int threshold);

    [LoggerMessage(
        EventId = 5202,
        Level = LogLevel.Warning,
        Message = "Liveness check failed ({Count} of {Threshold} consecutive).")]
    public static partial void LivenessCheckFailed(ILogger logger, int count, int threshold);

    [LoggerMessage(
        EventId = 5203,
        Level = LogLevel.Critical,
        Message = "Liveness watchdog exiting with code 70 after {Count} consecutive failures.")]
    public static partial void LivenessWatchdogExiting(ILogger logger, int count);

    [LoggerMessage(
        EventId = 5204,
        Level = LogLevel.Error,
        Message = "The liveness probe did not answer within its timeout, or threw.")]
    public static partial void LivenessProbeDidNotAnswer(ILogger logger, Exception exception);
}
