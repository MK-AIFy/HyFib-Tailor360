using System.ComponentModel.DataAnnotations;

namespace Tailor360.Worker;

/// <summary>Configuration for the background processing host.</summary>
public sealed class WorkerOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Worker";

    /// <summary>
    /// The port the health probes listen on. The worker serves no business traffic, so its probes live
    /// on their own port that is bound to the internal network only.
    /// </summary>
    [Range(1, 65535)]
    public int HealthPort { get; set; } = 8081;

    /// <summary>
    /// How often this instance writes its heartbeat. The readiness check treats a heartbeat older than
    /// three intervals as unhealthy, so a wedged instance is detected without a false alarm on a single
    /// slow write.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:05:00")]
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>A stable name for this instance, used as the heartbeat key. Defaults to the machine name.</summary>
    public string InstanceName { get; set; } = Environment.MachineName;
}
