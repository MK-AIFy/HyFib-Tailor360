using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Observability.Telemetry;

/// <summary>Telemetry configuration. Binding fails at startup when a value is out of range.</summary>
public sealed class ObservabilityOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Observability";

    /// <summary>The service name reported to the collector.</summary>
    [Required]
    public string ServiceName { get; set; } = "tailor360";

    /// <summary>
    /// The OTLP collector endpoint. Empty is the supported default: the application exports nothing and
    /// relies on structured logs, which is what a single-VM deployment without a telemetry backend needs
    /// (D14). A dead-man's switch outside the application watches for the absence of signal.
    /// </summary>
    public string OtlpEndpoint { get; set; } = string.Empty;

    /// <summary>The fraction of traces sampled, from 0 to 1.</summary>
    [Range(0d, 1d)]
    public double TraceSamplingRatio { get; set; } = 0.1d;

    /// <summary>True when an OTLP endpoint has been configured.</summary>
    public bool ExportsTelemetry => !string.IsNullOrWhiteSpace(OtlpEndpoint);
}
