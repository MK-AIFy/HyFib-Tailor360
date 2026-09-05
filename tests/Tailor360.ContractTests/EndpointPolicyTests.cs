using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.ContractTests;

/// <summary>
/// The endpoint inventory. These are the rules that decide whether a route is reachable and whether its
/// use is recorded, so they are checked against the routes the application actually publishes rather
/// than against source text.
/// </summary>
[Collection(WebHostCollection.Name)]
[Trait("Category", "Contract")]
public sealed class EndpointPolicyTests(WebHostFixture fixture)
{
    /// <summary>
    /// ARCH-007: every endpoint either demands authorisation or records why it is anonymous. An endpoint
    /// that simply omits both is the way a route becomes public by accident.
    /// </summary>
    [Fact]
    public void Arch007_EveryEndpointDeclaresAPolicyOrAJustifiedAnonymousExposure()
    {
        var undeclared = new List<string>();

        foreach (var endpoint in Endpoints())
        {
            var metadata = endpoint.Metadata;

            var requiresAuthorisation = metadata.GetMetadata<IAuthorizeData>() is not null;
            var justifiedAnonymous = metadata.GetMetadata<AnonymousJustificationMetadata>() is not null;
            var healthProbe = metadata.GetMetadata<HealthProbeMetadataMarker>() is not null
                || IsHealthProbe(endpoint);

            if (!requiresAuthorisation && !justifiedAnonymous && !healthProbe)
            {
                undeclared.Add(Describe(endpoint));
            }
        }

        undeclared.ShouldBeEmpty(
            "ARCH-007: these endpoints declare neither an authorisation policy nor a justified " +
            "anonymous exposure:\n" + string.Join('\n', undeclared));
    }

    /// <summary>
    /// ARCH-008: every state-changing endpoint is audited. The rule is asserted over the live route
    /// table so that it starts holding the moment the first command endpoint is added.
    /// </summary>
    [Fact]
    public void Arch008_EveryStateChangingEndpointIsAudited()
    {
        string[] stateChangingMethods = ["POST", "PUT", "PATCH", "DELETE"];
        var unaudited = new List<string>();

        foreach (var endpoint in Endpoints())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            if (!methods.Any(m => stateChangingMethods.Contains(m, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (endpoint.Metadata.GetMetadata<AuditedEndpointMetadata>() is null)
            {
                unaudited.Add(Describe(endpoint));
            }
        }

        unaudited.ShouldBeEmpty(
            "ARCH-008: these state-changing endpoints are not audited:\n" + string.Join('\n', unaudited));
    }

    /// <summary>
    /// Every anonymous exposure names where it was reviewed, so that the set can be re-examined when the
    /// threat model changes.
    /// </summary>
    [Fact]
    public void EveryAnonymousExposureRecordsItsReview()
    {
        foreach (var endpoint in Endpoints())
        {
            var justification = endpoint.Metadata.GetMetadata<AnonymousJustificationMetadata>();
            if (justification is null)
            {
                continue;
            }

            justification.Justification.Length.ShouldBeGreaterThan(
                20, $"{Describe(endpoint)} has an anonymous justification that says too little.");
            justification.ReviewedIn.ShouldNotBeNullOrWhiteSpace();
        }
    }

    /// <summary>The route table is not empty, so a mistake that mapped nothing would not pass silently.</summary>
    [Fact]
    public void TheApplicationPublishesRoutes()
        => Endpoints().Count.ShouldBeGreaterThan(3);

    private List<RouteEndpoint> Endpoints()
    {
        using var scope = fixture.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();
        return [.. sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()];
    }

    private static bool IsHealthProbe(RouteEndpoint endpoint)
        => endpoint.RoutePattern.RawText?.StartsWith("/health/", StringComparison.Ordinal) == true;

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        return $"{string.Join('|', methods)} {endpoint.RoutePattern.RawText}";
    }

    /// <summary>Placeholder so the rule reads the same way once probes gain explicit metadata.</summary>
    private sealed class HealthProbeMetadataMarker;
}
