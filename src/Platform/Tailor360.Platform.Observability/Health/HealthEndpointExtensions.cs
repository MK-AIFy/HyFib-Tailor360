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

    /// <summary>Path of the detailed health report.</summary>
    public const string DetailPath = "/health/detail";

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

    /// <summary>
    /// Maps <c>/health/detail</c>: every registered check, named, with its status, duration and
    /// description. Unlike the three probes above, this route carries no access policy of its own —
    /// the two hosts need different ones (permissioned on the web host, unauthenticated on the
    /// worker's unpublished port), so the caller declares it by chaining onto the returned builder.
    /// Deliberately carries no <see cref="HealthProbeMetadata"/>: that marker is what exempts a route
    /// from the rate-limit rule and from the endpoint-inventory's health-probe count, and this route is
    /// permissioned and documented as internal rather than exempted from either.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="buildVersion">The host's own build version, reported on every response.</param>
    public static IEndpointConventionBuilder MapHealthDetailEndpoint(
        this IEndpointRouteBuilder endpoints,
        string buildVersion)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildVersion);

        return endpoints.MapHealthChecks(DetailPath, new HealthCheckOptions
        {
            // Every registered check, including the two tagged NonEssential that no probe predicate
            // above accepts — they already run on every request to the three probes above, and this is
            // where their result is finally read by somebody.
            Predicate = _ => true,
            ResponseWriter = (context, report) => HealthDetailResponseWriter.WriteAsync(context, report, buildVersion),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        });
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
