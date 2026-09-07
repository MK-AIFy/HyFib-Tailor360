using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Platform.Observability.Health;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Web.Configuration;

namespace Tailor360.ContractTests;

/// <summary>
/// ARCH-017: every endpoint declares exactly one rate-limit policy from the catalogue.
/// </summary>
/// <remarks>
/// The assertion is over the composed route table rather than over source text, because a policy is
/// endpoint metadata: it can be declared on a route, on a group the route belongs to, or by a
/// convention the host applies to everything, and only the built application knows what each route
/// ended up with.
/// </remarks>
[Collection(WebHostCollection.Name)]
[Trait("Category", "Contract")]
public sealed class EndpointRateLimitTests(WebHostFixture fixture)
{
    /// <summary>The policy names the host registers a limiter for.</summary>
    private static HashSet<string> Catalogue =>
        RateLimitPolicies.Catalogue.Select(policy => policy.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The rule itself. An endpoint with no policy is an unmetered one; an endpoint naming a policy the
    /// host does not register throws on its first request.
    /// </summary>
    [Fact]
    public void Arch017_EveryEndpointDeclaresExactlyOneRateLimitPolicy()
    {
        var complaints = RateLimitPolicyInspector.Inspect(
            RateLimitPolicyInspector.Read(Endpoints()),
            Catalogue);

        complaints.ShouldBeEmpty(
            "ARCH-017 is breached:\n" + string.Join('\n', complaints));
    }

    /// <summary>
    /// The catalogue an endpoint may choose from and the catalogue the host registers are the same set.
    /// A name published in <see cref="RateLimitPolicyNames"/> that the host does not register is a trap:
    /// it compiles, it reviews as correct, and it throws the first time a request reaches the endpoint
    /// that declared it.
    /// </summary>
    [Fact]
    public void ThePublishedNamesAndTheRegisteredPoliciesAreTheSameSet()
    {
        var published = typeof(RateLimitPolicyNames)
            .GetFields()
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        published.ShouldNotBeEmpty();
        Catalogue.ShouldBe(published, ignoreOrder: true);
    }

    /// <summary>
    /// Every registered policy admits at least one request and bounds the traffic in a window that is
    /// neither unbounded nor so short that a fixed window behaves as no window at all.
    /// </summary>
    [Fact]
    public void EveryPolicyInTheCatalogueIsBounded()
    {
        foreach (var policy in RateLimitPolicies.Catalogue)
        {
            policy.Permits.ShouldBeGreaterThan(0, $"'{policy.Name}' would refuse every request.");
            policy.Window.ShouldBeGreaterThanOrEqualTo(
                TimeSpan.FromSeconds(1), $"'{policy.Name}' has a window shorter than a second.");
            policy.Window.ShouldBeLessThanOrEqualTo(
                TimeSpan.FromHours(1), $"'{policy.Name}' counts over a window nobody would notice.");
        }
    }

    /// <summary>
    /// The credential policies are keyed on the client address. Keying them on the account would mean a
    /// caller who has not signed in — which is every caller of a sign-in endpoint — shares one partition
    /// with everybody else who has not, or gets a private one per attempt; neither bounds guessing.
    /// </summary>
    [Fact]
    public void TheCredentialPoliciesAreKeyedOnTheClientAddress()
    {
        string[] credentialPolicies =
        [
            RateLimitPolicyNames.AuthenticationAnonymous,
            RateLimitPolicyNames.RecoveryAnonymous,
            RateLimitPolicyNames.DefaultIp,
        ];

        foreach (var name in credentialPolicies)
        {
            var policy = RateLimitPolicies.Catalogue.Single(candidate => candidate.Name == name);
            policy.KeyedOnAccount.ShouldBeFalse(
                $"'{name}' answers callers who have no account to key on.");
        }
    }

    /// <summary>
    /// The rejections that are recorded are the credential ones, and each names a policy that exists.
    /// </summary>
    [Fact]
    public void EveryAuditedPolicyIsInTheCatalogue()
        => RateLimitPolicies.AuditedPolicies.ShouldAllBe(policy => Catalogue.Contains(policy));

    /// <summary>Negative control: an endpoint that declares nothing is caught.</summary>
    [Fact]
    public void Arch017DetectorCatchesAnEndpointWithNoPolicy()
    {
        var complaints = RateLimitPolicyInspector.Inspect(
            RateLimitPolicyInspector.Read([Route("/api/v1/unmetered")]),
            Catalogue);

        complaints.ShouldHaveSingleItem().ShouldContain("declares no rate-limit policy");
    }

    /// <summary>Negative control: an endpoint that declares two policies is caught.</summary>
    [Fact]
    public void Arch017DetectorCatchesAnEndpointWithTwoPolicies()
    {
        var endpoint = Route(
            "/api/v1/twice",
            new EnableRateLimitingAttribute(RateLimitPolicyNames.Write),
            new EnableRateLimitingAttribute(RateLimitPolicyNames.DefaultUser));

        var complaints = RateLimitPolicyInspector.Inspect(
            RateLimitPolicyInspector.Read([endpoint]),
            Catalogue);

        complaints.ShouldHaveSingleItem().ShouldContain("declares 2 rate-limit policies");
    }

    /// <summary>Negative control: a policy the host registers no limiter for is caught.</summary>
    [Fact]
    public void Arch017DetectorCatchesAPolicyThatIsNotInTheCatalogue()
    {
        var endpoint = Route("/api/v1/invented", new EnableRateLimitingAttribute("invented-policy"));

        var complaints = RateLimitPolicyInspector.Inspect(
            RateLimitPolicyInspector.Read([endpoint]),
            Catalogue);

        complaints.ShouldHaveSingleItem().ShouldContain("registers no limiter for");
    }

    /// <summary>Negative control: opting out of the limiter is caught, policy or no policy.</summary>
    [Fact]
    public void Arch017DetectorCatchesAnEndpointThatDisablesRateLimiting()
    {
        var endpoint = Route(
            "/api/v1/opted-out",
            new EnableRateLimitingAttribute(RateLimitPolicyNames.Write),
            new DisableRateLimitingAttribute());

        var complaints = RateLimitPolicyInspector.Inspect(
            RateLimitPolicyInspector.Read([endpoint]),
            Catalogue);

        complaints.ShouldHaveSingleItem().ShouldContain("declares DisableRateLimiting");
    }

    /// <summary>
    /// Positive control: the detector is not simply complaining about everything. One catalogued policy
    /// passes, and so does a health probe with none — the exception the rule allows.
    /// </summary>
    [Fact]
    public void Arch017DetectorAcceptsOnePolicyAndExemptsAHealthProbe()
    {
        Endpoint[] endpoints =
        [
            Route("/api/v1/metered", new EnableRateLimitingAttribute(RateLimitPolicyNames.DefaultUser)),
            Route("/health/live", new HealthProbeMetadata("live")),
        ];

        RateLimitPolicyInspector.Inspect(RateLimitPolicyInspector.Read(endpoints), Catalogue)
            .ShouldBeEmpty();
    }

    private static RouteEndpoint Route(string pattern, params object[] metadata) => new(
        _ => Task.CompletedTask,
        RoutePatternFactory.Parse(pattern),
        order: 0,
        new EndpointMetadataCollection([new HttpMethodMetadata(["GET"]), .. metadata]),
        pattern);

    private List<Endpoint> Endpoints()
    {
        using var scope = fixture.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();
        return [.. sources.SelectMany(source => source.Endpoints)];
    }
}
