using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tailor360.Platform.Observability.Health;

/// <summary>
/// Writes the detailed health report: every registered check, named, with its status, duration and
/// description, and the host's build version. Reachable only by <c>admin.health.read</c> on the web
/// host and from the worker's unpublished port, so it may say more than the three anonymous probes —
/// but not everything. A check's <see cref="HealthReportEntry.Exception"/> is never read here: an
/// exception message can name a host, a connection string or a value the caller sent, and
/// <c>docs/nfr/data-classification.md</c> section 8 forbids all three in a health payload. Each check
/// is responsible for writing a safe <see cref="HealthReportEntry.Description"/> instead —
/// <c>DatabaseHealthCheck</c> already does exactly this for the one dependency whose failure is most
/// likely to try to say something specific.
/// </summary>
public static class HealthDetailResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>Writes the report as a detailed JSON document.</summary>
    public static async Task WriteAsync(HttpContext context, HealthReport report, string buildVersion)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildVersion);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        var payload = new HealthDetailPayload(
            report.Status.ToString(),
            (int)report.TotalDuration.TotalMilliseconds,
            buildVersion,
            [.. report.Entries.Select(entry => new HealthDetailComponent(
                entry.Key,
                entry.Value.Status.ToString(),
                (int)entry.Value.Duration.TotalMilliseconds,
                entry.Value.Description,
                [.. entry.Value.Tags]))]);

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private sealed record HealthDetailPayload(
        string Status,
        int DurationMs,
        string Version,
        IReadOnlyList<HealthDetailComponent> Components);

    private sealed record HealthDetailComponent(
        string Name,
        string Status,
        int DurationMs,
        string? Description,
        IReadOnlyList<string> Tags);
}
