using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Web.Configuration;

namespace Tailor360.Web.Endpoints;

/// <summary>The build and environment description the client shell reads on start-up.</summary>
public static class VersionEndpoints
{
    /// <summary>The route the progressive web application polls.</summary>
    public const string Path = "/api/version";

    /// <summary>
    /// Maps the version endpoint. It is anonymous because the application shell reads it before a
    /// session exists, and it discloses only the build hash and the environment name — never
    /// configuration, hostnames or dependency versions.
    /// </summary>
    public static IEndpointRouteBuilder MapVersionEndpoint(
        this IEndpointRouteBuilder endpoints,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(environment);

        endpoints.MapGet(Path, () => Results.Ok(new VersionResponse(
                BuildInformation.Version,
                BuildInformation.BuildHash,
                environment.EnvironmentName.ToLowerInvariant())))
            .AllowAnonymousWithJustification(
                "The application shell reads the build hash and environment before a session exists, " +
                "so that it can show the training banner and offer a safe reload after a deployment. " +
                "The response contains no configuration and no personal data.",
                "#20")
            .WithName("GetVersion")
            .WithTags("Platform");

        return endpoints;
    }
}

/// <summary>The build description returned to the client.</summary>
/// <param name="Version">The informational version of the running build.</param>
/// <param name="BuildHash">The short source revision, used to detect a stale client.</param>
/// <param name="Environment">
/// The environment name in lower case. Anything other than <c>production</c> makes the shell show the
/// persistent "TRAINING — not real data" banner.
/// </param>
public sealed record VersionResponse(string Version, string BuildHash, string Environment);
