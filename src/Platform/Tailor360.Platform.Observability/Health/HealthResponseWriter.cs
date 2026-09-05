using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tailor360.Platform.Observability.Health;

/// <summary>
/// Writes a health response that names components and their status and nothing else. Health endpoints
/// are reachable from the internal network without a session, so they must never disclose connection
/// strings, hostnames, versions of dependencies, exception messages or stack traces.
/// </summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    /// <summary>Writes the report as a minimal JSON document.</summary>
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        var payload = new HealthPayload(
            report.Status.ToString(),
            (int)report.TotalDuration.TotalMilliseconds,
            [.. report.Entries.Select(entry => new HealthComponent(entry.Key, entry.Value.Status.ToString()))]);

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private sealed record HealthPayload(string Status, int DurationMs, IReadOnlyList<HealthComponent> Components);

    private sealed record HealthComponent(string Name, string Status);
}
