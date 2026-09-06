using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.ContractTests;

/// <summary>
/// The declaration rules for endpoints whose authorisation depends on the row they name, checked over
/// the routes the application publishes and over routes deliberately declared wrongly.
/// </summary>
[Collection(WebHostCollection.Name)]
[Trait("Category", "Contract")]
public sealed class EndpointResourceScopeTests(WebHostFixture fixture)
{
    [Fact]
    public void EveryPublishedEndpointDeclaresItsAuthorisationConsistently()
    {
        var complaints = EndpointAuthorisationInspector.Inspect(Endpoints(), Catalogue());

        complaints.ShouldBeEmpty(
            "endpoints declare their authorisation inconsistently:\n" + string.Join('\n', complaints));
    }

    [Fact]
    public void NoPublishedEndpointDemandsAPermissionNoModuleDeclares()
    {
        // Stated on its own because it is the failure with no symptom: RequirePermission takes a string,
        // and a mistyped one denies every caller for ever without anything else looking wrong.
        var catalogue = Catalogue();

        var unknown =
            from endpoint in Endpoints()
            let permission = endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()
            where permission is not null && !catalogue.Contains(permission.PermissionKey)
            select $"{endpoint.RoutePattern.RawText} demands '{permission.PermissionKey}'";

        unknown.ShouldBeEmpty();
    }

    [Fact]
    public void TheInspectorCatchesAPermissionNoModuleDeclares()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .RequirePermission("orders.thsi_is_a_typo"),
            "no module declares",
            // Two: the permission does not exist, and the route names an identifier with no resource
            // scope behind it (ARCH-023). Both are true of this declaration and both are worth printing.
            expectedComplaints: 2);

    [Fact]
    public void TheInspectorCatchesAResourceScopeWithNoPermissionBehindIt()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .ScopedToResource("orders.garment_job", "jobId"),
            "declares a resource scope and no permission");

    [Fact]
    public void TheInspectorCatchesAResourceScopeThatDisagreesWithItsPermission()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .RequirePermission(OrdersPermissions.Read, BranchScope.CurrentBranch)
                .ScopedToResource("orders.garment_job", "jobId", BranchScope.Organisation),
            "The two are one decision");

    [Fact]
    public void TheInspectorCatchesAnAssignmentRequirementWithNothingToReadItFrom()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .RequirePermission(OrdersPermissions.Read)
                .RequireAssignment(OrdersPermissions.Assign),
            "declares no resource to read it from",
            // Two, and they are the same omission seen from two rules: with no resource declared there
            // is nothing to read an assignment from (this rule) and nothing to read a branch from
            // (ARCH-023).
            expectedComplaints: 2);

    [Fact]
    public void TheInspectorCatchesAnEndpointNarrowedToItsAssigneeAndNothingElse()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .ScopedToResource("orders.garment_job", "jobId")
                .RequireAssignment(OrdersPermissions.Assign),
            "does not decide who may reach the endpoint",
            // Two, because both verbs declare a policy and neither declares a permission: the endpoint
            // would pass ARCH-007 while demanding nothing of the caller but that they be somebody.
            expectedComplaints: 2);

    [Fact]
    public void TheInspectorCatchesASupervisingPermissionNoModuleDeclares()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .RequirePermission(OrdersPermissions.Read)
                .ScopedToResource("orders.garment_job", "jobId")
                .RequireAssignment("orders.assgin"),
            "supervising permission");

    [Fact]
    public void TheInspectorCatchesAnAnonymousEndpointReachingForAResource()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .AllowAnonymousWithJustification(
                    "A deliberately wrong declaration used only by this test.", "docs/security/field-visibility.md")
                .ScopedToResource("orders.garment_job", "jobId"),
            "anonymous and declares a resource scope",
            // Two, because it breaks two rules: an anonymous endpoint has no permission either, and
            // both complaints are worth printing rather than one masking the other.
            expectedComplaints: 2);

    /// <summary>ARCH-022, proved to detect.</summary>
    /// <remarks>
    /// It is one line away from happening for real: a group carrying <c>RequirePermission</c> with one
    /// child endpoint opting out of authentication reads as permission-gated in the source and in the
    /// matrix, and is open on the wire.
    /// </remarks>
    [Fact]
    public void TheInspectorCatchesAnEndpointThatIsBothPermissionedAndAnonymous()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs", () => Results.Ok())
                .RequirePermission(OrdersPermissions.Read)
                .AllowAnonymousWithJustification(
                    "A deliberately wrong declaration used only by this test.", "docs/security/field-visibility.md"),
            "AllowAnonymous wins at run time");

    /// <summary>ARCH-023, proved to detect.</summary>
    /// <remarks>
    /// This is the control the rule most needs. No module publishes a permissioned route yet, so without
    /// a deliberately wrong one the rule would run over an empty set for the whole of this issue and the
    /// next, and nobody would find out whether it works until it mattered.
    /// </remarks>
    [Fact]
    public void TheInspectorCatchesAPermissionedRouteWithAnIdentifierAndNoResourceScope()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .RequirePermission(OrdersPermissions.Read, BranchScope.CurrentBranch),
            "declares no resource scope");

    /// <summary>
    /// The same rule for the wider branch reach, because the branch check is just as absent there.
    /// </summary>
    [Fact]
    public void TheInspectorCatchesAnAssignedBranchesRouteWithAnIdentifierAndNoResourceScope()
        => Detects(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .RequirePermission(OrdersPermissions.Read, BranchScope.AssignedBranches),
            "declares no resource scope");

    /// <summary>
    /// And the exemptions, which have to pass or the rule would be unusable: a route with no identifier
    /// in it, an organisation-scoped route, and one that says out loud why it names no branch-owned row.
    /// </summary>
    [Fact]
    public void TheInspectorAcceptsTheThreeWaysARouteNeedsNoResourceScope()
    {
        InspectSynthetic(
            builder => builder.MapGet("/api/v1/jobs", () => Results.Ok())
                .RequirePermission(OrdersPermissions.Read)).ShouldBeEmpty();

        InspectSynthetic(
            builder => builder.MapGet("/api/v1/flags/{key}", () => Results.Ok())
                .RequirePermission(PlatformPermissions.FeatureFlags, BranchScope.Organisation)).ShouldBeEmpty();

        InspectSynthetic(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .RequirePermission(OrdersPermissions.Read)
                .TouchesNoBranchOwnedResource(
                    "A deliberately exempted declaration used only by this test.",
                    "docs/security/permission-matrix.md")).ShouldBeEmpty();
    }

    [Fact]
    public void TheInspectorPassesACorrectlyDeclaredEndpoint()
    {
        // The other half of a negative control: a detector that flagged everything would also pass every
        // test above and be worth nothing.
        var complaints = InspectSynthetic(
            builder => builder.MapGet("/api/v1/jobs/{jobId}", () => Results.Ok())
                .RequirePermission(OrdersPermissions.PhaseTransition)
                .ScopedToResource("orders.garment_job", "jobId")
                .RequireAssignment(OrdersPermissions.Assign, PlatformPermissions.ReadAllBranches));

        complaints.ShouldBeEmpty(string.Join('\n', complaints));
    }

    private static void Detects(
        Action<IEndpointRouteBuilder> declare,
        string expected,
        int expectedComplaints = 1)
    {
        var complaints = InspectSynthetic(declare);

        complaints.Count.ShouldBe(
            expectedComplaints, "got:\n" + string.Join('\n', complaints));
        complaints.ShouldContain(complaint => complaint.Contains(expected, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> InspectSynthetic(Action<IEndpointRouteBuilder> declare)
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();
        services.AddTailor360PermissionCatalogue();

        using var provider = services.BuildServiceProvider();
        var builder = new StandaloneRouteBuilder(provider);
        declare(builder);

        var endpoints = builder.DataSources.SelectMany(source => source.Endpoints).ToArray();
        endpoints.Length.ShouldBe(1, "the synthetic declaration published no route to inspect");

        return EndpointAuthorisationInspector.Inspect(
            endpoints, provider.GetRequiredService<PermissionCatalogue>());
    }

    /// <summary>
    /// A route builder with no host behind it, so that a deliberately wrong declaration can be built and
    /// inspected without starting an application that would refuse to start with it.
    /// </summary>
    private sealed class StandaloneRouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = services;

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private PermissionCatalogue Catalogue()
    {
        using var scope = fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PermissionCatalogue>();
    }

    private List<RouteEndpoint> Endpoints()
    {
        using var scope = fixture.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();
        return [.. sources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()];
    }
}
