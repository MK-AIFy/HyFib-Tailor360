using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Observability.Health;
using Tailor360.Platform.Security.Background;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// The worker's own registration of the shared <see cref="LivenessWatchdogService"/>: a one-line subclass
/// carrying <c>[WorkerJob]</c>, which architecture rule ARCH-021 requires of every hosted service the
/// worker composes. It runs as the system, holding no permission — a liveness check acts for nobody and
/// touches no branch-owned row.
/// </summary>
[WorkerJob(JobName, WorkerBranchScope.None)]
public sealed class WorkerLivenessWatchdogService(
    HealthCheckService healthCheckService,
    IOptions<LivenessWatchdogOptions> options,
    ILogger<WorkerLivenessWatchdogService> logger)
    : LivenessWatchdogService(healthCheckService, options, logger)
{
    /// <summary>The declared job name.</summary>
    public const string JobName = "platform.liveness_watchdog";
}
