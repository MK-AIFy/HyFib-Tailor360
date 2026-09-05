namespace Tailor360.Platform.Persistence.Entities;

/// <summary>
/// The last sign of life from a worker instance. Recorded per instance rather than globally, because a
/// single row would be kept fresh by whichever instance still worked and would hide the one that did not.
/// </summary>
public sealed class WorkerHeartbeat
{
    /// <summary>The instance name.</summary>
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>When it last beat.</summary>
    public DateTimeOffset LastBeatAt { get; set; }

    /// <summary>The build the instance is running, so a partial deployment is visible.</summary>
    public string Version { get; set; } = string.Empty;
}
