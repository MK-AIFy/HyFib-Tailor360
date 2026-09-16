using System.ComponentModel.DataAnnotations;

namespace Tailor360.Web.Configuration;

/// <summary>
/// How much of a client telemetry batch this server accepts, and whether it accepts one at all.
/// </summary>
/// <remarks>
/// Versioned configuration, not code (DOR-11): the numbers here are <b>proposed, to be confirmed</b>
/// against the flush interval and batch size <c>[E12-F03-5]</c> configures on the client, so neither side
/// writes a figure into code that the other has already decided. <see cref="MaxBodyBytes"/> is a second
/// line behind the host's own request-size limit, not a replacement for it: the host refuses an
/// oversized request before this endpoint is ever reached, and this option lets the endpoint refuse a
/// smaller batch with a reason a client can read, rather than the caller meeting an unexplained
/// connection reset.
/// </remarks>
public sealed class ClientTelemetryOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "ClientTelemetry";

    /// <summary>
    /// Whether the endpoint accepts telemetry at all. When false the route still answers <c>202</c> and
    /// emits nothing, so a client never changes behaviour because an operator turned ingestion off.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The most events one batch may carry.</summary>
    [Range(1, 500)]
    public int MaxEventsPerBatch { get; set; } = 50;

    /// <summary>The most bytes one batch's body may be.</summary>
    [Range(1024, 1_048_576)]
    public int MaxBodyBytes { get; set; } = 32_768;
}
