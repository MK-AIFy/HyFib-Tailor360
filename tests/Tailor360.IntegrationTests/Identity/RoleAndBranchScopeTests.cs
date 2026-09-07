using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Infrastructure.Access;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// The role model against a real <c>identity</c> schema: seeding, reconciliation, and the resolution
/// that turns a session cookie into a permission set and a branch scope.
/// </summary>
/// <remarks>
/// This is the tier that matters for #24's persistence half, because the questions it answers are all
/// questions about the database: does the composite key stop a grant being made twice, does the seeder
/// leave a custom role alone, and does the ticket the request pipeline sees actually carry the
/// permissions the roles grant — which it did not, by design, until this issue.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SessionDatabaseCollection.Name)]
public sealed class RoleAndBranchScopeTests(SessionDatabaseFixture fixture)
{
    private static readonly Guid OrganisationId = SessionTestData.OrganisationId;
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeedsEverySystemRoleAndIsIdempotent()
    {
        Assert.SkipUnless(SessionDatabaseFixture.IsAvailable, DatabaseAvailability.SkipReason);

        var connectionString = await fixture.CreateDatabaseAsync("roleseed");
        await using var context = SessionDatabaseFixture.CreateContext(connectionString);

        var first = await SeederFor(context).SeedSystemRolesAsync(
            OrganisationId, TestContext.Current.CancellationToken);

        first.RolesCreated.ShouldBe(SystemRoles.All.Count);
        first.RolesUpdated.ShouldBe(0);
        first.PermissionsGranted.ShouldBe(SystemRoles.All.Sum(role => role.Permissions.Count));

        var second = await SeederFor(context).SeedSystemRolesAsync(
            OrganisationId, TestContext.Current.CancellationToken);

        // The second run is the one that matters: a seeder that is not idempotent is a seeder nobody
        // can run against a live database, which makes it useless for the one job it has.
        second.RolesCreated.ShouldBe(0);
        second.RolesUpdated.ShouldBe(0);
        second.RolesUnchanged.ShouldBe(SystemRoles.All.Count);
        second.PermissionsGranted.ShouldBe(0);
        second.PermissionsRevoked.ShouldBe(0);

        var stored = await context.Roles.AsNoTracking()
            .Where(role => role.OrganisationId == OrganisationId)
            .ToListAsync(TestContext.Current.CancellationToken);

        stored.Count.ShouldBe(SystemRoles.All.Count);
        stored.ShouldAllBe(role => role.IsSystem);
        stored.Select(role => role.Key).Order(StringComparer.Ordinal)
            .ShouldBe(SystemRoles.All.Select(role => role.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EverySeededGrantNamesAPermissionTheApplicationDeclares()
    {
        Assert.SkipUnless(SessionDatabaseFixture.IsAvailable, DatabaseAvailability.SkipReason);

        var connectionString = await fixture.CreateDatabaseAsync("rolegrants");
        await using var context = SessionDatabaseFixture.CreateContext(connectionString);
        await SeederFor(context).SeedSystemRolesAsync(OrganisationId, TestContext.Current.CancellationToken);

        var catalogue = new PermissionCatalogue([new ApplicationPermissions()]);
        var stored = await context.RolePermissions.AsNoTracking()
            .Select(permission => permission.PermissionKey)
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);

        stored.Where(key => !catalogue.Contains(key)).ShouldBeEmpty();
        stored.Count.ShouldBe(catalogue.All.Count);
    }

    [Fact]
    public async Task RestoresAGrantRemovedByHandAndRemovesOneAddedByHand()
    {
        Assert.SkipUnless(SessionDatabaseFixture.IsAvailable, DatabaseAvailability.SkipReason);

        var connectionString = await fixture.CreateDatabaseAsync("rolereconcile");
        await using var context = SessionDatabaseFixture.CreateContext(connectionString);
        await SeederFor(context).SeedSystemRolesAsync(OrganisationId, TestContext.Current.CancellationToken);

        var tailor = await LoadRoleAsync(context, SystemRoles.Tailor);
        tailor.Revoke(CustodyPermissions.Scan, Now, by: null);
        tailor.Grant(PlatformPermissions.AuditRead, Now, by: null).IsSuccess.ShouldBeTrue();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outcome = await SeederFor(context).SeedSystemRolesAsync(
            OrganisationId, TestContext.Current.CancellationToken);

        outcome.RolesUpdated.ShouldBe(1);
        outcome.PermissionsGranted.ShouldBe(1);
        outcome.PermissionsRevoked.ShouldBe(1);

        context.ChangeTracker.Clear();
        var reconciled = await LoadRoleAsync(context, SystemRoles.Tailor);
        reconciled.Grants(CustodyPermissions.Scan).ShouldBeTrue();
        reconciled.Grants(PlatformPermissions.AuditRead).ShouldBeFalse();
    }

    [Fact]
    public async Task LeavesACustomRoleAlone()
    {
        Assert.SkipUnless(SessionDatabaseFixture.IsAvailable, DatabaseAvailability.SkipReason);

        var connectionString = await fixture.CreateDatabaseAsync("rolecustom");
        await using var context = SessionDatabaseFixture.CreateContext(connectionString);
        await SeederFor(context).SeedSystemRolesAsync(OrganisationId, TestContext.Current.CancellationToken);

        var custom = Role.Define(
            Guid.CreateVersion7(), OrganisationId, "senior_cashier", "Senior Cashier",
            "A role the shop invented.", RoleReach.Branch, Now).Value;
        custom.Grant(BillingPermissions.PostInvoice, Now, by: null).IsSuccess.ShouldBeTrue();
        context.Roles.Add(custom);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await SeederFor(context).SeedSystemRolesAsync(OrganisationId, TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var reloaded = await LoadRoleAsync(context, "senior_cashier");
        reloaded.IsSystem.ShouldBeFalse();
        reloaded.PermissionKeys.ShouldBe([BillingPermissions.PostInvoice]);
    }

    [Fact]
    public async Task ResolvesTheRolesPermissionsAndBranchesOfAnAccount()
    {
        Assert.SkipUnless(SessionDatabaseFixture.IsAvailable, DatabaseAvailability.SkipReason);

        var connectionString = await fixture.CreateDatabaseAsync("roleresolve");
        await using var context = SessionDatabaseFixture.CreateContext(connectionString);
        await SeederFor(context).SeedSystemRolesAsync(OrganisationId, TestContext.Current.CancellationToken);

        var branch = await AddBranchAsync(context, "MAIN", "Main Branch");
        var user = await SessionTestData.CreateActiveUserAsync(context, Now, "kavitha", "Kavitha S", branch.Id);
        await AssignAsync(context, user.Id, SystemRoles.Reception, branch.Id);

        var access = await new UserAccessQuery(context).ResolveAsync(
            user.Id, TestContext.Current.CancellationToken);

        access.RoleNames.ShouldBe(["Reception"]);
        access.BranchIds.ShouldBe([branch.Id]);
        access.Permissions.ShouldContain(OrdersPermissions.Confirm);
        access.Permissions.ShouldContain(CustomersPermissions.CaptureMeasurements);

        // The negative half, which is the half that matters: Reception is not the Cashier and is not
        // the Branch Manager, whatever else it can do.
        access.Permissions.ShouldNotContain(BillingPermissions.Session);
        access.Permissions.ShouldNotContain(OrdersPermissions.Reschedule);
        access.Permissions.ShouldNotContain(PlatformPermissions.ReadAllBranches);
    }

    [Fact]
    public async Task AnAccountWithNoRolesResolvesToNothing()
    {
        Assert.SkipUnless(SessionDatabaseFixture.IsAvailable, DatabaseAvailability.SkipReason);

        var connectionString = await fixture.CreateDatabaseAsync("rolenone");
        await using var context = SessionDatabaseFixture.CreateContext(connectionString);
        await SeederFor(context).SeedSystemRolesAsync(OrganisationId, TestContext.Current.CancellationToken);

        var user = await SessionTestData.CreateActiveUserAsync(context, Now, "nobody", "No Body");

        var access = await new UserAccessQuery(context).ResolveAsync(
            user.Id, TestContext.Current.CancellationToken);

        access.RoleNames.ShouldBeEmpty();
        access.Permissions.ShouldBeEmpty();
        access.BranchIds.ShouldBeEmpty();
    }

    /// <summary>
    /// The end-to-end shape: a cookie resolves to a ticket that carries the permissions the holder's
    /// roles grant and the branches they are assigned to. Until this issue the ticket carried an empty
    /// permission set by construction, so every permission-demanding endpoint denied everyone.
    /// </summary>
    [Fact]
    public async Task TheSessionTicketCarriesTheResolvedPermissionsAndBranches()
    {
        Assert.SkipUnless(SessionDatabaseFixture.IsAvailable, DatabaseAvailability.SkipReason);

        var connectionString = await fixture.CreateDatabaseAsync("roleticket");
        var clock = new TestClock(Now);

        Guid userId;
        Guid mainBranchId;
        Guid secondBranchId;

        await using (var context = SessionDatabaseFixture.CreateContext(connectionString))
        {
            await SeederFor(context).SeedSystemRolesAsync(
                OrganisationId, TestContext.Current.CancellationToken);

            var main = await AddBranchAsync(context, "MAIN", "Main Branch");
            var second = await AddBranchAsync(context, "SECOND", "Second Branch");
            mainBranchId = main.Id;
            secondBranchId = second.Id;

            var user = await SessionTestData.CreateActiveUserAsync(
                context, Now, "priya", "Priya K", main.Id);
            userId = user.Id;

            await AssignAsync(context, user.Id, SystemRoles.BranchManager, main.Id, second.Id);
        }

        await using var services = SessionDatabaseFixture.BuildServices(connectionString, clock);
        using var scope = services.CreateScope();

        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var started = await sessions.StartAsync(
            new StartSessionRequest(userId, "walkthrough device", MfaSatisfied: true),
            TestContext.Current.CancellationToken);

        started.IsSuccess.ShouldBeTrue();
        started.Value.SignInComplete.ShouldBeTrue();

        var store = scope.ServiceProvider.GetRequiredService<ISessionTicketStore>();
        var resolution = await store.ResolveAsync(
            started.Value.Token, TestContext.Current.CancellationToken);

        var ticket = resolution.Ticket.ShouldNotBeNull();
        ticket.Permissions.ShouldContain(OrdersPermissions.Reschedule);
        ticket.Permissions.ShouldContain(CustodyPermissions.ApproveReconciliation);
        ticket.Permissions.ShouldNotContain(PlatformPermissions.ReadAllBranches);
        ticket.AssignedBranches.ShouldBe([mainBranchId, secondBranchId], ignoreOrder: true);
    }

    /// <summary>
    /// A home branch is a default, not a grant. Somebody transferred out of a branch — their assignment
    /// removed while the column still names it — must not reach it.
    /// </summary>
    /// <remarks>
    /// This is the branch-transfer case the product describes, and it used to be the one case the
    /// session ticket got wrong: the ticket unioned <c>users.home_branch_id</c> into the assigned set,
    /// so removing the assignment stopped short of removing the reach. It also gave two answers to one
    /// question, because <c>IUserAccessQuery</c> — which every background job reads — never unioned it.
    /// </remarks>
    [Fact]
    public async Task AHomeBranchWhoseAssignmentWasRemovedIsNoLongerReachable()
    {
        Assert.SkipUnless(SessionDatabaseFixture.IsAvailable, DatabaseAvailability.SkipReason);

        var connectionString = await fixture.CreateDatabaseAsync("rolehomebranch");
        var clock = new TestClock(Now);

        Guid userId;
        Guid formerBranchId;
        Guid currentBranchId;

        await using (var context = SessionDatabaseFixture.CreateContext(connectionString))
        {
            await SeederFor(context).SeedSystemRolesAsync(
                OrganisationId, TestContext.Current.CancellationToken);

            var former = await AddBranchAsync(context, "FORMER", "Former Branch");
            var current = await AddBranchAsync(context, "CURRENT", "Current Branch");
            formerBranchId = former.Id;
            currentBranchId = current.Id;

            // The home branch stays where it was, which is exactly the state an administrator leaves
            // behind by editing assignments and not the user record.
            var user = await SessionTestData.CreateActiveUserAsync(
                context, Now, "transferred", "Transferred Person", former.Id);

            userId = user.Id;

            await AssignAsync(context, user.Id, SystemRoles.Tailor, current.Id);
        }

        await using var services = SessionDatabaseFixture.BuildServices(connectionString, clock);
        using var scope = services.CreateScope();

        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var started = await sessions.StartAsync(
            new StartSessionRequest(userId, "transfer device", MfaSatisfied: true),
            TestContext.Current.CancellationToken);

        started.IsSuccess.ShouldBeTrue();

        var store = scope.ServiceProvider.GetRequiredService<ISessionTicketStore>();
        var resolution = await store.ResolveAsync(
            started.Value.Token, TestContext.Current.CancellationToken);

        var ticket = resolution.Ticket.ShouldNotBeNull();
        ticket.AssignedBranches.ShouldBe([currentBranchId]);
        ticket.AssignedBranches.ShouldNotContain(formerBranchId);

        // And the two readers of "where does this person work" agree, which is the property that keeps
        // an HTTP request and a background job from reaching different sets of branches.
        var access = scope.ServiceProvider.GetRequiredService<IUserAccessQuery>();
        var effective = await access.ResolveAsync(userId, TestContext.Current.CancellationToken);

        effective.BranchIds.ShouldBe(ticket.AssignedBranches, ignoreOrder: true);
    }

    private static IdentityReferenceDataSeeder SeederFor(IdentityDbContext context)
        => new IdentityReferenceDataSeeder(
            context,
            new PermissionCatalogue([new ApplicationPermissions()]),
            new TestClock(Now),
            new UuidV7IdGenerator());

    private static async Task<Role> LoadRoleAsync(IdentityDbContext context, string key)
        => await context.Roles
            .Include(role => role.Permissions)
            .SingleAsync(role => role.Key == key, TestContext.Current.CancellationToken);

    private static async Task<Branch> AddBranchAsync(IdentityDbContext context, string code, string name)
    {
        var branch = Branch.Open(Guid.CreateVersion7(), OrganisationId, code, name, Now).Value;
        context.Branches.Add(branch);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return branch;
    }

    private static async Task AssignAsync(
        IdentityDbContext context,
        Guid userId,
        string roleKey,
        params Guid[] branchIds)
    {
        var role = await LoadRoleAsync(context, roleKey);
        context.UserRoles.Add(UserRoleAssignment.Create(userId, role.Id, Now));

        for (var index = 0; index < branchIds.Length; index++)
        {
            context.UserBranchAssignments.Add(
                UserBranchAssignment.Create(userId, branchIds[index], Now, isPrimary: index == 0));
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
