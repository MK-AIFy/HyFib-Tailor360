namespace Tailor360.Worker.Jobs;

/// <summary>Exposes the last heartbeat so the health check can judge whether this instance is working.</summary>
public interface IHeartbeatMonitor
{
    /// <summary>When this instance last recorded a heartbeat, or null before the first one.</summary>
    DateTimeOffset? LastBeat { get; }

    /// <summary>How old a heartbeat may be before the instance is considered unhealthy.</summary>
    TimeSpan StaleAfter { get; }
}
