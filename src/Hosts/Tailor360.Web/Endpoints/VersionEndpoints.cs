using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Persistence.Migrating;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Web.Configuration;
using Tailor360.Web.OpenApi;

namespace Tailor360.Web.Endpoints;

/// <summary>The build, environment and compatibility description the client shell reads on start-up.</summary>
public static class VersionEndpoints
{
    /// <summary>The route the progressive web application polls.</summary>
    public const string Path = "/api/version";

    /// <summary>
    /// Maps the version endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is anonymous because the application shell reads it before a session exists, and it is
    /// deliberately outside <c>/api/v1</c>: it is how a client discovers which API versions this server
    /// speaks, so it cannot itself live inside one of them.
    /// </para>
    /// <para>
    /// Everything in the response is a fact about the server's own build. There is no configuration in
    /// it, no hostname, no dependency version and no personal data — and the commit is withheld outside
    /// Development, because a precise revision tells an unauthenticated caller exactly which published
    /// advisories apply to what is running.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapVersionEndpoint(
        this IEndpointRouteBuilder endpoints,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(environment);

        endpoints.MapGet(Path, (
                IOptions<ClientCompatibilityOptions> compatibility,
                ModuleContextRegistry contexts) =>
                Results.Ok(Describe(environment, compatibility.Value, contexts)))
            .AllowAnonymousWithJustification(
                "The application shell reads the API version, the minimum client this server supports " +
                "and the environment before a session exists, so that it can refuse to start against a " +
                "server it cannot talk to, show the training banner and offer a safe reload after a " +
                "deployment. The response contains no configuration and no personal data.",
                "#20")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultIp)
            .WithName("GetVersion")
            .WithSummary("Report the running build, the supported client range and the environment.")
            .Produces<VersionResponse>(StatusCodes.Status200OK)
            .WithTags("Platform");

        return endpoints;
    }

    /// <summary>Builds the response for one environment.</summary>
    /// <remarks>
    /// Separated from the mapping so that what the endpoint discloses can be asserted per environment.
    /// The one member that varies is the revision, and the environment it varies by is the one an
    /// in-process host is hardest to change — so a test that could only reach this through the pipeline
    /// would end up asserting Development against Development and proving nothing.
    /// </remarks>
    /// <param name="environment">The hosting environment.</param>
    /// <param name="compatibility">The configured client-compatibility range.</param>
    /// <param name="contexts">The registered module contexts, for the schema version.</param>
    public static VersionResponse Describe(
        IHostEnvironment environment,
        ClientCompatibilityOptions compatibility,
        ModuleContextRegistry contexts)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(compatibility);
        ArgumentNullException.ThrowIfNull(contexts);

        return new VersionResponse(
            ApiDocument.Major,
            compatibility.MinimumClientVersion,
            BuildInformation.Version,
            environment.EnvironmentName.ToLowerInvariant(),
            SchemaVersion.Of(contexts),

            // Withheld everywhere but Development: a precise revision tells an unauthenticated caller
            // exactly which published advisories apply to what is running.
            environment.IsDevelopment() ? BuildInformation.BuildHash : null);
    }
}

/// <summary>The build and compatibility description returned to the client.</summary>
/// <param name="Api">
/// The major version of the staff API this server serves, for example <c>v1</c>. A client built
/// against a different major refuses to start rather than failing one request at a time.
/// </param>
/// <param name="MinimumClient">
/// The oldest client build this server answers. A client below it is refused with <c>426</c>, so a
/// client at or below it shows the update prompt rather than waiting to be refused.
/// </param>
/// <param name="Current">
/// The build the server is serving. A client whose own version differs knows a newer one is available
/// and can offer the reload at a moment the person chooses, which is what stops a shop-floor device
/// reloading in the middle of a measurement.
/// </param>
/// <param name="Environment">
/// The environment name in lower case. Anything other than <c>production</c> makes the shell show the
/// persistent "TRAINING — not real data" banner.
/// </param>
/// <param name="SchemaVersion">
/// The newest migration timestamp this build carries. It changes only when the database shape changes,
/// which is what a client with cached payloads needs in order to tell a routine deployment from one
/// that may have changed what those payloads mean.
/// </param>
/// <param name="Commit">
/// The short source revision, in Development only; null — and absent from the response — everywhere
/// else.
/// </param>
public sealed record VersionResponse(
    string Api,
    string MinimumClient,
    string Current,
    string Environment,
    string SchemaVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Commit);
