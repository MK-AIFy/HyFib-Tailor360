using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// Authorisation decided against the resource rather than against the caller's claims.
/// </summary>
/// <remarks>
/// Every case here is a request that the branch-scope requirement alone would have allowed. That is the
/// point of the tier: a signed-in tailor asking for another branch's job passes every check that only
/// looks at the caller, because there is nothing wrong with the caller.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ResourceScopeAuthorisationTests
{
    private static readonly Guid BranchA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");
    private static readonly Guid BranchB = Guid.Parse("0199a000-0000-7000-8000-00000000000b");
    private static readonly Guid BranchC = Guid.Parse("0199a000-0000-7000-8000-00000000000c");
    private static readonly Guid JobId = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    private static readonly Guid Tailor = Guid.Parse("0199a000-0000-7000-8000-0000000000f1");
    private static readonly Guid AnotherTailor = Guid.Parse("0199a000-0000-7000-8000-0000000000f2");

    [Fact]
    public async Task GrantsWhenTheResourceIsInABranchTheCallerWorksIn()
    {
        var context = await EvaluateBranchAsync(
            InBranch(BranchA),
            Resolved(BranchA),
            BranchScope.CurrentBranch);

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniesWhenTheResourceBelongsToAnotherBranch()
    {
        // The caller is impeccable: signed in, assigned to their branch, acting in it. The row is not
        // theirs, and only the row can say so.
        var context = await EvaluateBranchAsync(
            InBranch(BranchA),
            Resolved(BranchB),
            BranchScope.CurrentBranch);

        context.HasFailed.ShouldBeTrue();
        Refusal(context).ShouldBe(AuthorisationRefusal.ResourceUnreachable);
    }

    [Fact]
    public async Task DeniesAnIdentifierThatMatchesNothingTheSameWayAsOneBelongingToSomebodyElse()
    {
        var missing = await EvaluateBranchAsync(
            InBranch(BranchA), new ResourceScopeContext().Missing(), BranchScope.CurrentBranch);
        var foreign = await EvaluateBranchAsync(
            InBranch(BranchA), Resolved(BranchB), BranchScope.CurrentBranch);

        missing.HasFailed.ShouldBeTrue();
        Refusal(missing).ShouldBe(Refusal(foreign));
    }

    [Fact]
    public async Task DeniesWhenTheResolutionStepNeverRan()
    {
        // A host assembled without UseTailor360ResourceScope() must refuse, not wave the request past
        // the check it was supposed to perform.
        var context = await EvaluateBranchAsync(
            InBranch(BranchA), new ResourceScopeContext(), BranchScope.CurrentBranch);

        context.HasFailed.ShouldBeTrue();
        Refusal(context).ShouldBe(AuthorisationRefusal.PipelineIncomplete);
    }

    [Fact]
    public async Task DeniesWhenTheEndpointRequiresAResourceAndTheStepFoundNoneDeclared()
    {
        var scope = new ResourceScopeContext();
        scope.SetNotRequired();

        var context = await EvaluateBranchAsync(InBranch(BranchA), scope, BranchScope.CurrentBranch);

        context.HasFailed.ShouldBeTrue();
        Refusal(context).ShouldBe(AuthorisationRefusal.PipelineIncomplete);
    }

    [Fact]
    public async Task DeniesAnUnauthenticatedCaller()
    {
        var user = new TestUser { IsAuthenticated = false, AssignedBranches = { BranchA } };

        var context = await EvaluateBranchAsync(user, Resolved(BranchA), BranchScope.CurrentBranch);

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task LetsOrganisationWideReachReadAnotherBranchsResource()
    {
        var owner = InBranch(BranchA);
        owner.Permissions.Add(PlatformPermissions.ReadAllBranches);

        var context = await EvaluateBranchAsync(owner, Resolved(BranchB), BranchScope.Organisation);

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DoesNotLetOrganisationWideReachWriteToAnotherBranchsResource()
    {
        // The permission is named read_all_branches and the name is the contract. A write declares
        // CurrentBranch, and holding the reading reach does not widen it.
        var owner = InBranch(BranchA);
        owner.Permissions.Add(PlatformPermissions.ReadAllBranches);

        var context = await EvaluateBranchAsync(owner, Resolved(BranchB), BranchScope.CurrentBranch);

        context.HasFailed.ShouldBeTrue();
        Refusal(context).ShouldBe(AuthorisationRefusal.ResourceUnreachable);
    }

    [Fact]
    public async Task DoesNotTreatManyBranchAssignmentsAsOrganisationWideReach()
    {
        var manager = new TestUser
        {
            IsAuthenticated = true,
            Context = new OrganisationContext(Guid.Empty, BranchA),
            AssignedBranches = { BranchA, BranchB },
        };

        var context = await EvaluateBranchAsync(manager, Resolved(BranchC), BranchScope.Organisation);

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task GrantsTheAssigneeTheirOwnJob()
    {
        var context = await EvaluateOwnershipAsync(
            InBranch(BranchA, Tailor), Resolved(BranchA, Tailor), [OrdersPermissions.Assign]);

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniesAJobAssignedToSomebodyElseEvenInTheCallersOwnBranch()
    {
        var context = await EvaluateOwnershipAsync(
            InBranch(BranchA, Tailor), Resolved(BranchA, AnotherTailor), [OrdersPermissions.Assign]);

        context.HasFailed.ShouldBeTrue();
        Refusal(context).ShouldBe(AuthorisationRefusal.NotAssigned);
    }

    [Fact]
    public async Task DeniesAnUnassignedJobToSomebodyWhoIsNotSupervising()
    {
        var context = await EvaluateOwnershipAsync(
            InBranch(BranchA, Tailor), Resolved(BranchA), [OrdersPermissions.Assign]);

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task LetsASupervisingPermissionSeePastTheAssignment()
    {
        var lead = InBranch(BranchA, AnotherTailor);
        lead.Permissions.Add(OrdersPermissions.Assign);

        var context = await EvaluateOwnershipAsync(
            lead, Resolved(BranchA, Tailor), [OrdersPermissions.Assign]);

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniesOwnershipWhenThereIsNoResolvedResource()
    {
        var context = await EvaluateOwnershipAsync(
            InBranch(BranchA, Tailor), new ResourceScopeContext(), [OrdersPermissions.Assign]);

        context.HasFailed.ShouldBeTrue();
        Refusal(context).ShouldBe(AuthorisationRefusal.PipelineIncomplete);
    }

    [Fact]
    public async Task DeniesOwnershipToAnUnauthenticatedCaller()
    {
        var user = new TestUser { IsAuthenticated = false, AssignedBranches = { BranchA } };

        var context = await EvaluateOwnershipAsync(
            user, Resolved(BranchA, Tailor), [OrdersPermissions.Assign]);

        context.HasFailed.ShouldBeTrue();
    }

    private static TestUser InBranch(Guid branchId, Guid? userId = null) => new()
    {
        IsAuthenticated = true,
        UserId = userId ?? Tailor,
        Context = new OrganisationContext(Guid.Empty, branchId),
        AssignedBranches = { branchId },
    };

    private static ResourceScopeContext Resolved(Guid branchId, params Guid[] assignees)
    {
        var scope = new ResourceScopeContext();
        scope.SetResolved(assignees.Length == 0
            ? ResourceScope.Unassigned("orders.garment_job", JobId, branchId)
            : new ResourceScope("orders.garment_job", JobId, branchId, new HashSet<Guid>(assignees)));

        return scope;
    }

    private static async Task<AuthorizationHandlerContext> EvaluateBranchAsync(
        TestUser user,
        ResourceScopeContext scope,
        BranchScope declared)
    {
        var requirement = new ResourceBranchRequirement(declared);
        var context = new AuthorizationHandlerContext(
            [requirement], new System.Security.Claims.ClaimsPrincipal(), null);

        await new ResourceBranchAuthorisationHandler(user, scope).HandleAsync(context);
        return context;
    }

    private static async Task<AuthorizationHandlerContext> EvaluateOwnershipAsync(
        TestUser user,
        ResourceScopeContext scope,
        IReadOnlyList<string> supervisors)
    {
        var requirement = new ResourceOwnershipRequirement(supervisors);
        var context = new AuthorizationHandlerContext(
            [requirement], new System.Security.Claims.ClaimsPrincipal(), null);

        await new ResourceOwnershipAuthorisationHandler(user, scope).HandleAsync(context);
        return context;
    }

    private static AuthorisationRefusal Refusal(AuthorizationHandlerContext context)
        => context.FailureReasons.OfType<RefusalReason>().Select(reason => reason.Refusal).Single();

    private sealed class TestUser : ICurrentUser
    {
        public bool IsAuthenticated { get; init; }

        public Guid UserId { get; init; } = Guid.Parse("0199a000-0000-7000-8000-0000000000ff");

        public string PrincipalId => UserId.ToString("n");

        public string DisplayName => "Test User";

        public OrganisationContext Context { get; init; } = new(Guid.Empty, null);

        public HashSet<Guid> AssignedBranches { get; init; } = [];

        public HashSet<string> Permissions { get; init; } = new(StringComparer.Ordinal);

        IReadOnlySet<Guid> ICurrentUser.AssignedBranches => AssignedBranches;

        IReadOnlySet<string> ICurrentUser.Permissions => Permissions;

        public bool MfaSatisfied { get; init; } = true;

        public bool IsSignInComplete { get; init; } = true;

        public DateTimeOffset? LastReauthenticatedAt { get; init; }

        public bool HasPermission(string permissionKey) => Permissions.Contains(permissionKey);

        public bool CanActInBranch(Guid branchId) => AssignedBranches.Contains(branchId);
    }
}

/// <summary>Small helpers that keep the arrangement of these tests to one line each.</summary>
internal static class ResourceScopeContextTestExtensions
{
    /// <summary>A context whose resolution step ran and found nothing.</summary>
    public static ResourceScopeContext Missing(this ResourceScopeContext context)
    {
        context.SetNotFound();
        return context;
    }
}
