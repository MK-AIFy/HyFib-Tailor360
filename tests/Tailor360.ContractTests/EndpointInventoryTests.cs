using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Tailor360.ContractTests;

/// <summary>
/// The endpoint inventory: an endpoint cannot exist without appearing in the published API document,
/// and the document cannot describe an endpoint that does not exist.
/// </summary>
/// <remarks>
/// This is the rule that keeps every other API gate honest. The lint checks what the document says, and
/// the diff gate checks how it changed; neither notices a route that was never in it. Reading the live
/// route table is what makes "the document is the contract" a fact rather than an intention.
/// </remarks>
[Collection(WebHostCollection.Name)]
[Trait("Category", "Contract")]
public sealed class EndpointInventoryTests(WebHostFixture fixture)
{
    [Fact]
    public async Task EveryPublishedEndpointAppearsInTheApiDocument()
    {
        var routes = Routes();
        var document = await ApiDocumentSource.GenerateAsync(
            fixture.Services, TestContext.Current.CancellationToken);
        var operations = PublishedEndpointInventory.ReadOperations(document);

        var complaints = PublishedEndpointInventory.Reconcile(routes, operations);

        complaints.ShouldBeEmpty(
            "The route table and the API document disagree:\n"
            + string.Join('\n', complaints.Select(complaint => "  " + complaint)));
    }

    /// <summary>
    /// Every route excluded from the document says why, so the exclusion set can be re-read the way the
    /// anonymous set is.
    /// </summary>
    [Fact]
    public void EveryInternalEndpointRecordsAReasonAndAReview()
    {
        using var scope = fixture.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();

        var declarations = sources
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => (
                endpoint,
                metadata: endpoint.Metadata
                    .GetMetadata<Tailor360.Platform.Security.Endpoints.InternalEndpointMetadata>()))
            .Where(pair => pair.metadata is not null)
            .ToList();

        declarations.ShouldNotBeEmpty(
            "no route declares .InternalEndpoint(...), which cannot be right while the shell fallback "
            + "and the 404 catch-alls exist.");

        foreach (var (endpoint, metadata) in declarations)
        {
            metadata!.Reason.Length.ShouldBeGreaterThan(
                20, $"{endpoint.DisplayName} excludes itself from the document and says too little.");
            metadata.ReviewedIn.ShouldNotBeNullOrWhiteSpace();
        }
    }

    /// <summary>The route table is not empty, so a mapping mistake cannot pass by describing nothing.</summary>
    [Fact]
    public void TheInventoryReadsTheWholeRouteTable()
    {
        var routes = Routes();

        routes.Count.ShouldBeGreaterThan(20);
        routes.Count(route => route.IsHealthProbe).ShouldBe(3);
        routes.Count(route => route.InternalReason is { Length: > 0 }).ShouldBeGreaterThanOrEqualTo(3);
    }

    // ---- Negative controls -------------------------------------------------------------------

    [Fact]
    public void TheReconciliationPassesWhenTheTwoSidesAgree()
        => PublishedEndpointInventory.Reconcile(
            [new("GET", "/api/v1/things", false, null, false)],
            [new("GET", "/api/v1/things", "ListThings")]).ShouldBeEmpty();

    [Fact]
    public void TheReconciliationDetectsAnEndpointMissingFromTheDocument()
    {
        var complaints = PublishedEndpointInventory.Reconcile(
            [new("GET", "/api/v1/things", false, null, false)],
            []);

        complaints.Select(complaint => complaint.Rule).ShouldContain("undocumented-endpoint");
    }

    [Fact]
    public void TheReconciliationDetectsAnExclusionThatGivesNoReason()
    {
        // ExcludeFromDescription on its own must not be a way out: an endpoint hidden from the document
        // is exactly the endpoint nobody reviews.
        var complaints = PublishedEndpointInventory.Reconcile(
            [new("POST", "/api/v1/things", true, null, false)],
            []);

        complaints.Select(complaint => complaint.Rule).ShouldContain("undeclared-exclusion");
    }

    [Fact]
    public void TheReconciliationAcceptsADeclaredInternalEndpoint()
        => PublishedEndpointInventory.Reconcile(
            [new("POST", "/api/v1/things", true, "It answers 404 and nothing else.", false)],
            []).ShouldBeEmpty();

    [Fact]
    public void TheReconciliationDetectsAnInternalEndpointThatIsDocumentedAnyway()
    {
        var complaints = PublishedEndpointInventory.Reconcile(
            [new("POST", "/api/v1/things", true, "It answers 404 and nothing else.", false)],
            [new("POST", "/api/v1/things", "CreateThing")]);

        complaints.Select(complaint => complaint.Rule).ShouldContain("internal-endpoint-documented");
    }

    [Fact]
    public void TheReconciliationAcceptsAHealthProbe()
        => PublishedEndpointInventory.Reconcile(
            [new("GET", "/health/live", true, null, true)],
            []).ShouldBeEmpty();

    [Fact]
    public void TheReconciliationDetectsADocumentedOperationNothingPublishes()
    {
        var complaints = PublishedEndpointInventory.Reconcile(
            [],
            [new("GET", "/api/v1/ghosts", "ListGhosts")]);

        complaints.Select(complaint => complaint.Rule).ShouldContain("phantom-operation");
    }

    [Theory]
    [InlineData("/api/v1/auth/passkeys/{passkeyId:guid}", "/api/v1/auth/passkeys/{passkeyId}")]
    [InlineData("/api/v1/me/", "/api/v1/me")]
    [InlineData("/api/{**path}", "/api/{path}")]
    [InlineData("{*path:nonfile}", "/{path}")]
    [InlineData("/api/v1/orders/{id:guid}/jobs/{jobId:guid}", "/api/v1/orders/{id}/jobs/{jobId}")]
    [InlineData("/", "/")]
    public void RouteTemplatesReduceToTheFormTheDocumentUses(string template, string expected)
        => PublishedEndpointInventory.NormalisePath(template).ShouldBe(expected);

    private IReadOnlyList<PublishedRoute> Routes()
    {
        using var scope = fixture.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();
        return PublishedEndpointInventory.ReadRoutes(sources);
    }
}
