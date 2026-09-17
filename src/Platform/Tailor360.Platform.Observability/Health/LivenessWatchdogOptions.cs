using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Observability.Health;

/// <summary>
/// The in-process liveness watchdog's own settings, bound from <c>LivenessWatchdog</c>.
/// <c>docs/architecture/container.md</c> and <c>docs/architecture/failure-modes.md</c> section 2 specify
/// the rule this configures rather than invent it here: three consecutive failures exit with code 70, and
/// the container's <c>restart: unless-stopped</c> policy brings it back.
/// </summary>
public sealed class LivenessWatchdogOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "LivenessWatchdog";

    /// <summary>How often the watchdog checks. Shorter than a container orchestrator's own health-check interval, so this fires first.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The watchdog's own timeout on one check. A check that has not answered by this point is treated as
    /// a failure whatever it eventually returns — this is what catches thread-pool starvation and a
    /// deadlocked process, which a check that only read the last reported status could never detect.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan CheckTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Consecutive failures, with no success between them, before the watchdog exits the process.</summary>
    [Range(1, 20)]
    public int ConsecutiveFailuresBeforeExit { get; set; } = 3;
}
