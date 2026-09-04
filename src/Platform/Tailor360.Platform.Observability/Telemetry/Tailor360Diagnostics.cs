using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Tailor360.Platform.Observability.Telemetry;

/// <summary>The single activity source and meter the application publishes under.</summary>
public static class Tailor360Diagnostics
{
    /// <summary>The name used for traces and metrics emitted by this application.</summary>
    public const string SourceName = "HyFib.Tailor360";

    /// <summary>The activity source for manually started spans.</summary>
    public static ActivitySource ActivitySource { get; } = new(SourceName);

    /// <summary>The meter for application metrics.</summary>
    public static Meter Meter { get; } = new(SourceName);
}
