using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Permissions;
using Tailor360.UnitTests.Security;

namespace Tailor360.IntegrationTests.Authorization;

/// <summary>
/// The generated authorisation matrix: the routes the application publishes, held equal to the rows the
/// owner approved in <c>docs/security/permission-matrix.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// The failure this exists to prevent is an endpoint that escapes review. A route added without a
/// matrix row is a route whose exposure nobody agreed to, and it is invisible precisely because
/// nothing points at it — no test names it, no document lists it, and the only way to notice is for
/// somebody to read the route table and wonder. So the test is generated from the route table rather
/// than written against it: it enumerates what the server will serve, and every one of them has to be
/// accounted for.
/// </para>
/// <para>
/// The negative controls below are the other half. Every assertion here is of the form "these two
/// collections agree", and two collections agree vacuously when one of them is empty — a parser that
/// silently read nothing, a route table that had not been composed yet. So the reconciler is exercised
/// against deliberately wrong inputs as well as against the real ones, and the real inputs are checked
/// for being non-empty before anything is concluded from them.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class AuthorisationMatrixTests(WebApplicationFixture fixture)
{
    private static readonly PermissionCatalogue Catalogue = new([new ApplicationPermissions()]);

    /// <summary>
    /// The assertion the issue is judged on: no route escapes the matrix, and no row survives the route
    /// it was written for.
    /// </summary>
    [Fact]
    public void TheApprovedMatrixAndThePublishedRoutesAgree()
    {
        var endpoints = PublishedEndpoints();
        var document = PermissionMatrixDocument.Load();
        var rows = AuthorisationMatrix.EndpointRows(document);

        // Guarded before the comparison, not after: "every published route has a row" is satisfied by
        // publishing no routes, and "every row names a route" by approving none.
        endpoints.ShouldNotBeEmpty("The application published no routes, so this test proves nothing.");
        rows.ShouldNotBeEmpty("The matrix's endpoint block is empty, so this test proves nothing.");

        var complaints = AuthorisationMatrix.Reconcile(rows, endpoints, Catalogue, ApprovedPermissions(document));

        complaints.ShouldBeEmpty(
            "The authorisation matrix and the route table disagree:\n  " + string.Join("\n  ", complaints));
    }

    /// <summary>
    /// Every route is classified as something. A route the inventory could not read would otherwise be
    /// absent from both sides of the comparison and pass by not being there.
    /// </summary>
    [Fact]
    public void EveryPublishedRouteDeclaresHowItDecidesWhoMayReachIt()
    {
        var undeclared = PublishedEndpoints()
            .Where(endpoint => endpoint.Kind == EndpointDeclarationKind.Undeclared)
            .Select(endpoint => endpoint.Signature)
            .ToArray();

        undeclared.ShouldBeEmpty(
            "These routes declare no permission, no justified anonymous exposure and no self-service "
            + "assurance level:\n  " + string.Join("\n  ", undeclared));
    }

    /// <summary>
    /// The one standing exemption is the orchestrator's probes, and it is an exemption by path rather
    /// than a judgement about any particular route.
    /// </summary>
    [Fact]
    public void TheOnlyRoutesExemptFromAPolicyAreTheHealthProbes()
    {
        var exempt = PublishedEndpoints()
            .Where(endpoint => endpoint.Kind == EndpointDeclarationKind.HealthProbe)
            .ToArray();

        exempt.ShouldNotBeEmpty("The health probes have gone; this exemption no longer describes anything.");
        exempt.ShouldAllBe(endpoint => endpoint.Route.StartsWith(EndpointInventory.HealthProbePrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// The self-service level is read off a metadata type by name, because the type belongs to a module
    /// this project does not reference. A rename would silently reclassify every self-service route as
    /// undeclared, so the reading is asserted to have found something.
    /// </summary>
    [Fact]
    public void TheSelfServiceLevelsAreStillBeingRead()
    {
        string[] known = ["live-session", "sign-in-complete", "second-factor-satisfied"];

        var selfService = PublishedEndpoints()
            .Where(endpoint => endpoint.Kind == EndpointDeclarationKind.SelfService)
            .ToArray();

        selfService.ShouldNotBeEmpty(
            "No route was read as self-service. Either the module stopped declaring them, or the "
            + "metadata type was renamed and the inventory is now reading nothing.");

        selfService.Select(endpoint => endpoint.Assurance).Distinct(StringComparer.Ordinal)
            .ShouldBeSubsetOf(known);
    }

    /// <summary>Every permission an endpoint demands is one the catalogue declares and the matrix approves.</summary>
    /// <remarks>
    /// Guarded against the vacuous pass in the same way as its siblings, and the guard is written so that
    /// it is honest today: no module publishes a permissioned route yet, so the set of demanded keys is
    /// empty and the two assertions below prove nothing on their own. What is asserted instead is that
    /// the number of routes demanding a permission equals the number of section 5 rows approved to — a
    /// statement that is true and non-trivial now, and becomes the real check the moment #32a maps the
    /// first one. The rule itself is proved to detect by the reconciler's own negative controls.
    /// </remarks>
    [Fact]
    public void EveryPermissionAnEndpointDemandsIsDeclaredAndApproved()
    {
        var document = PermissionMatrixDocument.Load();
        var approved = ApprovedPermissions(document);

        var published = PublishedEndpoints();
        published.ShouldNotBeEmpty("The application published no routes, so this test proves nothing.");

        var demanded = published
            .Select(endpoint => endpoint.PermissionKey)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var rowsDemandingOne = AuthorisationMatrix.EndpointRows(document)
            .Select(row => row.Permission)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        demanded.Order(StringComparer.Ordinal).ShouldBe(
            rowsDemandingOne.Order(StringComparer.Ordinal),
            "The permissions the routes demand and the permissions section 5 approves them to demand "
            + "are not the same set.");

        demanded.Where(key => !Catalogue.Contains(key)).ShouldBeEmpty();
        demanded.Where(key => !approved.Contains(key)).ShouldBeEmpty();
    }

    /// <summary>
    /// The fixtures point at where the field set per role is declared rather than restating it, and the
    /// pointer resolves.
    /// </summary>
    /// <remarks>
    /// Reach and detail are two questions. This matrix decides which rows a caller may touch; which
    /// columns come back is decided by a declared response view, from a different source. A pointer is
    /// the right answer and a rotting pointer is the wrong one, so it is checked.
    /// </remarks>
    [Fact]
    public void TheFixturesPointAtWhereTheFieldSetsAreDeclared()
    {
        var references = MatrixFixtures.Load().FieldSetReferences;

        references.ShouldNotBeEmpty();
        references.ShouldAllBe(path => File.Exists(MatrixFixtures.Resolve(path)));
    }

    /// <summary>
    /// The fixture reader refuses what it cannot read, rather than answering "no expectation".
    /// </summary>
    /// <remarks>
    /// An expectation nobody wrote down must not become a test that passes. These are the three ways
    /// that could happen — a file that is not one mapping, a dimension nobody described, and a dimension
    /// naming an answer nobody defined — and each throws.
    /// </remarks>
    [Fact]
    public void TheFixtureReaderRefusesWhatItCannotRead()
    {
        Should.Throw<InvalidOperationException>(() => MatrixFixtures.Parse("- not\n- a mapping\n"))
            .Message.ShouldContain("one YAML document");

        var minimal = MatrixFixtures.Parse(
            """
            answers:
              allowed:
                status: 200
                code: null
            dimensions:
              a: allowed
            """);

        minimal.Expected("a").Status.ShouldBe(System.Net.HttpStatusCode.OK);

        Should.Throw<InvalidOperationException>(() => minimal.Expected("no-such-dimension"))
            .Message.ShouldContain("is missing");

        Should.Throw<InvalidOperationException>(() => minimal.Answer("no-such-answer"))
            .Message.ShouldContain("no answer called");

        Should.Throw<InvalidOperationException>(() => minimal.OrganisationReachGaps.ToArray())
            .Message.ShouldContain("no 'organisation-reach-gaps' list");
    }

    // ---------------------------------------------------------------------------------------------
    // Negative controls. Each one feeds the reconciler an input that is wrong in exactly one way and
    // asserts it says so. Without these, every assertion above could be passing because the reconciler
    // returns an empty list whatever it is given.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DetectsARouteThatHasNoRowInTheMatrix()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [],
            [Anonymous("GET", "/api/v1/orders")],
            Catalogue,
            Approved());

        complaints.ShouldHaveSingleItem().ShouldContain("GET /api/v1/orders");
        complaints[0].ShouldContain("no row in the authorisation matrix");
    }

    [Fact]
    public void DetectsARowThatNamesNoPublishedRoute()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("GET", "/api/v1/orders", "anonymous")],
            [],
            Catalogue,
            Approved());

        complaints.ShouldHaveSingleItem().ShouldContain("which the application does not publish");
    }

    [Fact]
    public void DetectsTheSameRoutePublishedTwice()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("GET", "/api/v1/orders", "anonymous")],
            [Anonymous("GET", "/api/v1/orders"), Anonymous("GET", "/api/v1/orders")],
            Catalogue,
            Approved());

        complaints.ShouldContain(complaint =>
            complaint.Contains("publishes 'GET /api/v1/orders' more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectsTheSameRouteApprovedTwice()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("GET", "/api/v1/orders", "anonymous"), Row("GET", "/api/v1/orders", "anonymous")],
            [Anonymous("GET", "/api/v1/orders")],
            Catalogue,
            Approved());

        complaints.ShouldContain(complaint => complaint.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectsARouteApprovedAsAnonymousThatDemandsAPermission()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("POST", "/api/v1/orders", "anonymous")],
            [Permissioned("POST", "/api/v1/orders", OrdersPermissions.Confirm)],
            Catalogue,
            Approved(OrdersPermissions.Confirm));

        complaints.ShouldContain(complaint =>
            complaint.Contains("approved as 'anonymous' and enforced as 'permission'", StringComparison.Ordinal));
    }

    /// <summary>
    /// The one that matters most in the other direction: an endpoint that quietly swaps a demanding
    /// permission for a laxer one. The row still says what was approved, and the route no longer does it.
    /// </summary>
    [Fact]
    public void DetectsARouteThatDemandsADifferentPermissionFromTheApprovedOne()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("POST", "/api/v1/orders", "permission", CustodyPermissions.ApproveReconciliation,
                BranchScope.CurrentBranch)],
            [Permissioned("POST", "/api/v1/orders", OrdersPermissions.Confirm)],
            Catalogue,
            Approved(OrdersPermissions.Confirm, CustodyPermissions.ApproveReconciliation));

        complaints.ShouldContain(complaint =>
            complaint.Contains("approved to demand", StringComparison.Ordinal)
            && complaint.Contains(CustodyPermissions.ApproveReconciliation, StringComparison.Ordinal));
    }

    [Fact]
    public void DetectsARouteWhoseBranchReachIsWiderThanTheApprovedOne()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("GET", "/api/v1/orders", "permission", OrdersPermissions.Read, BranchScope.CurrentBranch)],
            [Permissioned("GET", "/api/v1/orders", OrdersPermissions.Read, BranchScope.Organisation)],
            Catalogue,
            Approved(OrdersPermissions.Read));

        complaints.ShouldContain(complaint =>
            complaint.Contains("branch scope 'current-branch' and declares 'organisation'", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectsARouteApprovedToDemandAPermissionNoModuleDeclares()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("POST", "/api/v1/orders", "permission", "orders.confrim", BranchScope.CurrentBranch)],
            [Permissioned("POST", "/api/v1/orders", "orders.confrim")],
            Catalogue,
            Approved("orders.confrim"));

        complaints.ShouldContain(complaint =>
            complaint.Contains("which no module declares", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectsARouteThatDemandsAPermissionTheMatrixNeverApproved()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("POST", "/api/v1/orders", "permission", OrdersPermissions.Confirm, BranchScope.CurrentBranch)],
            [Permissioned("POST", "/api/v1/orders", OrdersPermissions.Confirm)],
            Catalogue,
            Approved());

        complaints.ShouldContain(complaint =>
            complaint.Contains("has no row in the matrix's permission block", StringComparison.Ordinal));
    }

    /// <summary>ARCH-018, proved to detect.</summary>
    [Fact]
    public void DetectsAStepUpPermissionOnAnEndpointThatDoesNotDeclareStepUp()
    {
        Catalogue.Find(CustodyPermissions.ReprintLabel)!.RequiresStepUp.ShouldBeTrue(
            "This control depends on the permission being flagged; pick another if the flag moves.");

        var complaints = AuthorisationMatrix.Reconcile(
            [Row("POST", "/api/v1/labels", "permission", CustodyPermissions.ReprintLabel, BranchScope.CurrentBranch)],
            [Permissioned("POST", "/api/v1/labels", CustodyPermissions.ReprintLabel)],
            Catalogue,
            Approved(CustodyPermissions.ReprintLabel));

        complaints.ShouldContain(complaint => complaint.StartsWith("ARCH-018", StringComparison.Ordinal));
    }

    /// <summary>
    /// And the other way: an endpoint demanding a fresh re-authentication for a permission that is not
    /// flagged. Harmless to a caller and wrong to a reader, because the demand is then a property of one
    /// route rather than of the action, and the next route to use the permission will not have it.
    /// </summary>
    [Fact]
    public void DetectsStepUpDeclaredForAPermissionThatIsNotFlaggedForIt()
    {
        Catalogue.Find(OrdersPermissions.Confirm)!.RequiresStepUp.ShouldBeFalse();

        var complaints = AuthorisationMatrix.Reconcile(
            [Row("POST", "/api/v1/orders", "permission", OrdersPermissions.Confirm, BranchScope.CurrentBranch)],
            [Permissioned("POST", "/api/v1/orders", OrdersPermissions.Confirm, stepUp: true)],
            Catalogue,
            Approved(OrdersPermissions.Confirm));

        complaints.ShouldContain(complaint =>
            complaint.Contains("is not flagged for step-up", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectsAStateChangingRouteThatIsNotAuditedAsApproved()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("POST", "/api/v1/orders", "anonymous", audit: "orders.order.confirmed")],
            [Anonymous("POST", "/api/v1/orders")],
            Catalogue,
            Approved());

        complaints.ShouldContain(complaint =>
            complaint.Contains("approved to be audited as 'orders.order.confirmed'", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectsARouteThatDeclaresNothingAtAll()
    {
        var complaints = AuthorisationMatrix.Reconcile(
            [Row("GET", "/api/v1/orders", "undeclared")],
            [new EndpointDeclaration(
                "GET", "/api/v1/orders", EndpointDeclarationKind.Undeclared,
                null, null, null, false, null, false, null, false)],
            Catalogue,
            Approved());

        complaints.ShouldContain(complaint =>
            complaint.Contains("reachable and nobody has said by whom", StringComparison.Ordinal));
    }

    private IReadOnlyList<EndpointDeclaration> PublishedEndpoints()
        => EndpointInventory.Read(fixture.Services.GetRequiredService<IEnumerable<EndpointDataSource>>());

    private static HashSet<string> ApprovedPermissions(PermissionMatrixDocument document)
        => document.Section(AuthorisationMatrix.PermissionsSection).Rows
            .Select(row => row.Text("Permission"))
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Approved(params string[] keys) => keys.ToHashSet(StringComparer.Ordinal);

    private static MatrixEndpointRow Row(
        string method,
        string route,
        string declaration,
        string? permission = null,
        BranchScope? scope = null,
        string? audit = null)
        => new(method, route, declaration, permission, scope, null, audit);

    private static EndpointDeclaration Anonymous(string method, string route)
        => new(method, route, EndpointDeclarationKind.Anonymous, null, null, null, false, null, false, null, false);

    private static EndpointDeclaration Permissioned(
        string method,
        string route,
        string permission,
        BranchScope scope = BranchScope.CurrentBranch,
        bool stepUp = false)
        => new(method, route, EndpointDeclarationKind.Permission, permission, scope, null, false, null, false, null,
            stepUp);
}
