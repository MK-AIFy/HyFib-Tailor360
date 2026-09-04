using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tailor360.Platform.Abstractions.Health;

namespace Tailor360.Platform.Observability.Health;

/// <summary>Maps the three probes every host exposes.</summary>
public static class HealthEndpointExtensions
{
    /// <summary>Path of the liveness probe.</summary>
    public const string LivePath = "/health/live";

    /// <summary>Path of the readiness probe.</summary>
    public const string ReadyPath = "/health/ready";

    /// <summary>Path of the startup probe.</summary>
    public const string StartupPath = "/health/startup";

    /// <summary>
    /// Maps <c>/health/live</c>, <c>/health/ready</c> and <c>/health/startup</c>. The probes are
    /// deliberately anonymous because an orchestrator and a compose watchdog call them without a
    /// session; the response body carries component names and statuses only.
    /// </summary>
    public static IEndpointRouteBuilder MapTailor360HealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        Map(endpoints, LivePath, HealthCheckTags.Live);
        Map(endpoints, ReadyPath, HealthCheckTags.Ready);
        Map(endpoints, StartupPath, HealthCheckTags.Startup);

        return endpoints;
    }

    private static void Map(IEndpointRouteBuilder endpoints, string path, string tag)
    {
        endpoints.MapHealthChecks(path, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(tag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
            ResultStatusCodes =
            {
                // A degraded non-essential dependency still serves traffic, so it answers 200.
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        })
        .AllowAnonymous()
        .WithMetadata(new HealthProbeMetadata(tag))
        .ExcludeFromDescription();
    }
}

/// <summary>Marks an endpoint as a health probe so architecture tests can exempt it from the policy rule.</summary>
/// <param name="Tag">The probe tag this endpoint reports.</param>
public sealed record HealthProbeMetadata(string Tag);
