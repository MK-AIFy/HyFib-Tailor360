using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// Authorisation handlers. Every case here is one an attacker or a mistake would otherwise exploit, so
/// each is asserted to deny rather than merely to "not succeed".
/// </summary>
[Trait("Category", "Unit")]
public sealed class AuthorisationTests
{
    private static readonly Guid BranchA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");
    private static readonly Guid BranchB = Guid.Parse("0199a000-0000-7000-8000-00000000000b");
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GrantsWhenTheCallerHoldsThePermission()
    {
        var context = await EvaluatePermissionAsync(
            new TestUser { IsAuthenticated = true, Permissions = { "fake.read" } },
            new Permission("fake.read", "Read.", "Fake"));

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniesWhenTheCallerIsNotAuthenticated()
    {
        var context = await EvaluatePermissionAsync(
            new TestUser { IsAuthenticated = false, Permissions = { "fake.read" } },
            new Permission("fake.read", "Read.", "Fake"));

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniesWhenTheCallerDoesNotHoldThePermission()
    {
        var context = await EvaluatePermissionAsync(
            new TestUser { IsAuthenticated = true },
            new Permission("fake.read", "Read.", "Fake"));

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniesWhenTheEndpointNamesAPermissionNoModuleDeclares()
    {
        // A typo in an endpoint must close it, not open it.
        var handler = new PermissionAuthorisationHandler(
            new TestUser { IsAuthenticated = true, Permissions = { "fake.read" } },
            new PermissionCatalogue([]),
            new FixedClock(Now),
            Options.Create(new StepUpOptions()));

        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement("fake.read")], new System.Security.Claims.ClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        context.HasFailed.ShouldBeTrue();
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task DeniesAMultiFactorPermissionWhenTheSessionDidNotCompleteTheChallenge()
    {
        var context = await EvaluatePermissionAsync(
            new TestUser { IsAuthenticated = true, MfaSatisfied = false, Permissions = { "fake.pay" } },
            new Permission("fake.pay", "Pay.", "Fake", RequiresMfa: true));

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniesAStepUpPermissionWhenTheReauthenticationIsStale()
    {
        var context = await EvaluatePermissionAsync(
            new TestUser
            {
                IsAuthenticated = true,
                MfaSatisfied = true,
                LastReauthenticatedAt = Now.AddMinutes(-6),
                Permissions = { "fake.refund" },
            },
            new Permission("fake.refund", "Refund.", "Fake", RequiresMfa: true, RequiresStepUp: true));

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task GrantsAStepUpPermissionWhenTheReauthenticationIsFresh()
    {
        var context = await EvaluatePermissionAsync(
            new TestUser
            {
                IsAuthenticated = true,
                MfaSatisfied = true,
                LastReauthenticatedAt = Now.AddMinutes(-1),
                Permissions = { "fake.refund" },
            },
            new Permission("fake.refund", "Refund.", "Fake", RequiresMfa: true, RequiresStepUp: true));

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniesAStepUpPermissionWhenTheCallerNeverReauthenticated()
    {
        var context = await EvaluatePermissionAsync(
            new TestUser
            {
                IsAuthenticated = true,
                MfaSatisfied = true,
                LastReauthenticatedAt = null,
                Permissions = { "fake.refund" },
            },
            new Permission("fake.refund", "Refund.", "Fake", RequiresStepUp: true));

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task DeniesWorkOutsideTheCallersAssignedBranch()
    {
        var user = new TestUser
        {
            IsAuthenticated = true,
            Context = new OrganisationContext(Guid.Empty, BranchB),
            AssignedBranches = { BranchA },
        };

        var context = await EvaluateBranchScopeAsync(user, BranchScope.CurrentBranch);

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task GrantsWorkInsideTheCallersAssignedBranch()
    {
        var user = new TestUser
        {
            IsAuthenticated = true,
            Context = new OrganisationContext(Guid.Empty, BranchA),
            AssignedBranches = { BranchA },
        };

        var context = await EvaluateBranchScopeAsync(user, BranchScope.CurrentBranch);

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DoesNotTreatManyBranchAssignmentsAsOrganisationWideReach()
    {
        // Holding every branch today is not the same as being allowed to see the organisation, because
        // the branch list changes without anyone revisiting the authorisation decision.
        var user = new TestUser
        {
            IsAuthenticated = true,
            Context = new OrganisationContext(Guid.Empty, BranchA),
            AssignedBranches = { BranchA, BranchB },
        };

        var context = await EvaluateBranchScopeAsync(user, BranchScope.Organisation);

        context.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task GrantsOrganisationWideReachOnlyWithTheOrganisationPermission()
    {
        var user = new TestUser
        {
            IsAuthenticated = true,
            Context = new OrganisationContext(Guid.Empty, BranchA),
            AssignedBranches = { BranchA },
            Permissions = { BranchScopeAuthorisationHandler.OrganisationWidePermission },
        };

        var context = await EvaluateBranchScopeAsync(user, BranchScope.Organisation);

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task TheDefaultPrincipalHoldsNothing()
    {
        var anonymous = new AnonymousCurrentUser();

        anonymous.IsAuthenticated.ShouldBeFalse();
        anonymous.HasPermission(PlatformPermissions.AuditRead).ShouldBeFalse();
        anonymous.CanActInBranch(BranchA).ShouldBeFalse();

        var context = await EvaluateBranchScopeAsync(anonymous, BranchScope.AssignedBranches);
        context.HasFailed.ShouldBeTrue();
    }

    private static async Task<AuthorizationHandlerContext> EvaluatePermissionAsync(
        Tailor360.Platform.Security.Authorisation.ICurrentUser user,
        Permission permission)
    {
        var handler = new PermissionAuthorisationHandler(
            user,
            new PermissionCatalogue([new SingleSource(permission)]),
            new FixedClock(Now),
            Options.Create(new StepUpOptions()));

        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(permission.Key)], new System.Security.Claims.ClaimsPrincipal(), null);

        await handler.HandleAsync(context);
        return context;
    }

    private static async Task<AuthorizationHandlerContext> EvaluateBranchScopeAsync(
        Tailor360.Platform.Security.Authorisation.ICurrentUser user,
        BranchScope scope)
    {
        var handler = new BranchScopeAuthorisationHandler(user);
        var context = new AuthorizationHandlerContext(
            [new BranchScopeRequirement(scope)], new System.Security.Claims.ClaimsPrincipal(), null);

        await handler.HandleAsync(context);
        return context;
    }

    private sealed class SingleSource(Permission permission) : IPermissionSource
    {
        public IReadOnlyCollection<Permission> Permissions { get; } = [permission];
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;

        public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, branchTimeZone).DateTime);
    }

    private sealed class TestUser : Tailor360.Platform.Security.Authorisation.ICurrentUser
    {
        public bool IsAuthenticated { get; init; }

        public Guid UserId { get; init; } = Guid.Parse("0199a000-0000-7000-8000-0000000000ff");

        public string PrincipalId => UserId.ToString();

        public string DisplayName => "Test User";

        public OrganisationContext Context { get; init; } = new(Guid.Empty, BranchA);

        public HashSet<Guid> AssignedBranches { get; init; } = [];

        public HashSet<string> Permissions { get; init; } = new(StringComparer.Ordinal);

        IReadOnlySet<Guid> Tailor360.Platform.Security.Authorisation.ICurrentUser.AssignedBranches
            => AssignedBranches;

        IReadOnlySet<string> Tailor360.Platform.Security.Authorisation.ICurrentUser.Permissions
            => Permissions;

        public bool MfaSatisfied { get; init; }

        public DateTimeOffset? LastReauthenticatedAt { get; init; }

        public bool HasPermission(string permissionKey) => Permissions.Contains(permissionKey);

        public bool CanActInBranch(Guid branchId) => AssignedBranches.Contains(branchId);
    }
}
