using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// The role register: defining a role, changing what it grants, and the four ways that is refused.
/// </summary>
/// <remarks>
/// Everything else on the administrative surface changes who holds what. These endpoints change what
/// holding it <em>means</em>, so most of what is worth asserting here is a refusal — the catalogue
/// boundary, the reach rule, the escalation guard and the lockout guard. Each of them is the difference
/// between "roles are configurable" and "authorisation is advisory".
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RoleAdministrationEndpointTests(WebApplicationFixture fixture)
{
    private const string Reason = "Approved at the September operations review.";

    /// <summary>A key shaped like a permission and declared by nobody.</summary>
    private static readonly string[] NotCatalogued = ["orders.do_whatever_i_like"];

    /// <summary>Whether a PostgreSQL instance was found for this run.</summary>
    public static bool Available => DatabaseAvailability.IsAvailable;

    private static int _enumClientNumber;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("Branch, Organisation")]
    [InlineData("Branch, Branch")]
    [InlineData("Unknown")]
    public async Task RoleReachMustNameOneDeclaredMember(string? reach)
    {
        Assert.SkipUnless(Available, DatabaseAvailability.SkipReason);
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-reachname", $"2001:db8:90:1::{Interlocked.Increment(ref _enumClientNumber):x}", Permissions.Roles);
        var key = $"invalid_{AdministrationHarness.UniqueToken(10)}";

        using var refused = await administrator.PostAsync(
            "/api/v1/admin/roles/",
            new { key, name = "Synthetic role", description = "Parser regression.", reach, reason = Reason },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("identity.role-reach-not-recognised");
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await context.Roles.AnyAsync(role => role.Key == key, TestContext.Current.CancellationToken))
            .ShouldBeFalse("an invalid reach must never become a stored role");
    }

    [Theory]
    [InlineData("branch", "Branch")]
    [InlineData(" oRgAnIsAtIoN ", "Organisation")]
    public async Task RoleReachAcceptsDeclaredNamesIgnoringCase(string reach, string expected)
    {
        Assert.SkipUnless(Available, DatabaseAvailability.SkipReason);
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-reachvalid", $"2001:db8:90:1::{Interlocked.Increment(ref _enumClientNumber):x}", Permissions.Roles);
        using var created = await administrator.PostAsync(
            "/api/v1/admin/roles/",
            new
            {
                key = $"valid_{AdministrationHarness.UniqueToken(10)}",
                name = "Synthetic role",
                description = "Parser regression.",
                reach,
                reason = Reason,
            },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await AuthenticationClient.ReadAsync<RoleBody>(created)).ShouldNotBeNull().Reach.ShouldBe(expected);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task ACustomRoleIsDefinedEmptyAndThenGivenWhatItGrants()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-role", "203.0.113.150", Permissions.Roles);

        var key = $"custom_{AdministrationHarness.UniqueToken(10)}";

        var defined = await administrator.PostAsync(
            "/api/v1/admin/roles/",
            new
            {
                key,
                name = "Senior counter",
                description = "A counter role for the branches that close their own session.",
                reach = "Organisation",
                reason = Reason,
            },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        defined.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await defined.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var role = (await AuthenticationClient.ReadAsync<RoleBody>(defined)).ShouldNotBeNull();
        role.Key.ShouldBe(key);
        role.IsSystem.ShouldBeFalse();
        role.Holders.ShouldBe(0);

        // Created granting nothing on purpose. The grants are the consequential part and go through the
        // permissions route, which is where the four invariants are checked.
        role.PermissionKeys.ShouldBeEmpty();

        // An organisation-reach role granted an organisation-scoped permission: the reach rule from its
        // passing side, with its refusing side asserted below. The key is one the harness's
        // administrator holds, because the escalation guard would otherwise refuse it for the other
        // reason and the test would pass for the wrong one.
        var granted = await administrator.PutAsync(
            $"/api/v1/admin/roles/{role.RoleId}/permissions",
            new { permissionKeys = new[] { PlatformPermissions.ReadAllBranches }, reason = Reason },
            ("If-Match", defined.Headers.ETag.ShouldNotBeNull().Tag),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        granted.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await granted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        (await AuthenticationClient.ReadAsync<RoleBody>(granted))
            .ShouldNotBeNull().PermissionKeys.ShouldHaveSingleItem()
            .ShouldBe(PlatformPermissions.ReadAllBranches);

        // The version the grant consumed is stale, and a second edit against it is refused rather than
        // silently reversing what the first one decided.
        var stale = await administrator.PutAsync(
            $"/api/v1/admin/roles/{role.RoleId}/permissions",
            new { permissionKeys = Array.Empty<string>(), reason = Reason },
            ("If-Match", defined.Headers.ETag.Tag),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task APermissionTheCatalogueDoesNotDeclareIsRefused()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-rolefake", "203.0.113.151", Permissions.Roles);

        var (roleId, version) = await CustomRoleAsync(administrator, "fake", RoleReach.Organisation);

        var refused = await administrator.PutAsync(
            $"/api/v1/admin/roles/{roleId}/permissions",
            new { permissionKeys = NotCatalogued, reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        problem.ShouldContain("identity.permission-not-catalogued");

        // Roles are data and the catalogue is code. A typo has to be a refusal, not a role that grants
        // nothing and looks on the screen as though it grants something.
        (await ReadAsync(administrator, roleId)).PermissionKeys.ShouldBeEmpty();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task AnOrganisationScopedPermissionIsRefusedOnABranchReachRole()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-rolereach", "203.0.113.152", Permissions.Roles);

        var (roleId, version) = await CustomRoleAsync(administrator, "reach", RoleReach.Branch);

        // The runtime half of the rule permission-matrix.md section 4 states, whose document test only
        // ever sees the seeded register. Without this the rule would hold for the roles the release
        // ships and stop holding the first time somebody used the screen built to change them.
        var refused = await administrator.PutAsync(
            $"/api/v1/admin/roles/{roleId}/permissions",
            new { permissionKeys = new[] { PlatformPermissions.AuditRead }, reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain("identity.permission-exceeds-role-reach");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task AnAdministratorCannotGrantAPermissionTheyDoNotThemselvesHold()
    {
        // The administrator holds admin.roles and organisation reach, and nothing else. Vertical
        // escalation is exactly this: somebody who may edit any role writing themselves a role that
        // grants anything the catalogue declares.
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-roleesc", "203.0.113.153", Permissions.Roles);

        var (roleId, version) = await CustomRoleAsync(administrator, "esc", RoleReach.Organisation);

        var refused = await administrator.PutAsync(
            $"/api/v1/admin/roles/{roleId}/permissions",
            new { permissionKeys = new[] { PlatformPermissions.AuditExport }, reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain("identity.permission-not-held-by-granter");

        (await ReadAsync(administrator, roleId)).PermissionKeys.ShouldBeEmpty();

        // The mirror image: they may grant what they do hold. Without this the test would also pass if
        // the endpoint refused everything, which is not the rule.
        var allowed = await administrator.PutAsync(
            $"/api/v1/admin/roles/{roleId}/permissions",
            new { permissionKeys = new[] { PlatformPermissions.ReadAllBranches }, reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        allowed.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await allowed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task TakingAwayAPermissionIsNotEscalationAndIsAllowed()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-roledrop", "203.0.113.154", Permissions.Roles);

        var (roleId, _) = await CustomRoleAsync(administrator, "drop", RoleReach.Organisation);

        // Seeded straight into the store, because the endpoint would refuse to put it there — which is
        // the whole point: an administrator below the Owner has to be able to reduce a role granting
        // things they do not hold, or the usual case becomes the impossible one.
        await GrantDirectlyAsync(roleId, PlatformPermissions.AuditExport);

        var current = await ReadAsync(administrator, roleId);
        current.PermissionKeys.ShouldContain(PlatformPermissions.AuditExport);

        var reduced = await administrator.PutAsync(
            $"/api/v1/admin/roles/{roleId}/permissions",
            new { permissionKeys = Array.Empty<string>(), reason = Reason },
            ("If-Match", await VersionOfAsync(administrator, roleId)),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        reduced.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await reduced.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        (await ReadAsync(administrator, roleId)).PermissionKeys.ShouldBeEmpty();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task AReplaceThatChangedNothingStillConsumesTheVersionItPresented()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-rolenoop", "203.0.113.161", Permissions.Roles);

        var (roleId, version) = await CustomRoleAsync(administrator, "noop", RoleReach.Organisation);

        // The set the role already has: an administrator who opened the screen, changed their mind and
        // pressed save anyway. The outcome is the same, and the request still happened.
        var applied = await administrator.PutAsync(
            $"/api/v1/admin/roles/{roleId}/permissions",
            new { permissionKeys = Array.Empty<string>(), reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        applied.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await applied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Somebody else still holding the version from before it is refused, rather than writing over
        // the top of a decision they never saw. Without the row being touched the aggregate would not
        // have stamped anything — nothing changed — and this would silently succeed.
        var second = await administrator.PutAsync(
            $"/api/v1/admin/roles/{roleId}/permissions",
            new { permissionKeys = new[] { PlatformPermissions.ReadAllBranches }, reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task ASystemRoleCannotBeDeletedAndNorCanOneSomebodyHolds()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-roledel", "203.0.113.155", Permissions.Roles);

        // The administrator's own role: a custom one this harness created, and one they hold.
        var held = await RoleOfAsync(administrator.UserId);

        var refusedHeld = await administrator.PostAsync(
            $"/api/v1/admin/roles/{held}/delete",
            new { reason = Reason },
            ("If-Match", await VersionOfAsync(administrator, held)),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        refusedHeld.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refusedHeld.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain("identity.role-still-held");

        var system = await SystemRoleAsync();

        var refusedSystem = await administrator.PostAsync(
            $"/api/v1/admin/roles/{system}/delete",
            new { reason = Reason },
            ("If-Match", await VersionOfAsync(administrator, system)),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        refusedSystem.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refusedSystem.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain("identity.system-role-not-deletable");

        // And the case that is allowed, so the two refusals are not simply "delete never works".
        var (spare, version) = await CustomRoleAsync(administrator, "spare", RoleReach.Branch);

        (await administrator.PostAsync(
                $"/api/v1/admin/roles/{spare}/delete",
                new { reason = Reason },
                ("If-Match", version),
                ("Idempotency-Key", Guid.CreateVersion7().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await administrator.GetAsync($"/api/v1/admin/roles/{spare}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task ARoleChangeIsRecordedWithWhatItGrantedBeforeAndAfter()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-roleaudit", "203.0.113.156", Permissions.Roles);

        using var auditor = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-rolereader", "203.0.113.157", Permissions.AuditRead);

        var (roleId, version) = await CustomRoleAsync(administrator, "audited", RoleReach.Organisation);

        (await administrator.PutAsync(
                $"/api/v1/admin/roles/{roleId}/permissions",
                new { permissionKeys = new[] { PlatformPermissions.ReadAllBranches }, reason = Reason },
                ("If-Match", version),
                ("Idempotency-Key", Guid.CreateVersion7().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var trail = await ReadTrailAsync(auditor, $"?entityType=Role&entityId={roleId}&limit=20");

        trail.Entries.ShouldContain(entry => entry.Action == "identity.role.permissions-replaced");
        var entry = trail.Entries.First(e => e.Action == "identity.role.permissions-replaced");

        entry.Reason.ShouldBe(Reason);
        entry.ActorId.ShouldBe(administrator.UserId);

        // The entry says what the role granted before, which is what makes "who gave them that" a
        // question the trail can answer rather than one that needs the previous night's backup.
        entry.After.ShouldNotBeNull().ShouldContain(PlatformPermissions.ReadAllBranches);
        entry.Before.ShouldNotBeNull().ShouldNotContain(PlatformPermissions.ReadAllBranches);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task TheCatalogueIsPublishedWithTheFlagsThatChangeWhatAnAdministratorIsDeciding()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-rolecat", "203.0.113.158", Permissions.Roles);

        var response = await administrator.GetAsync("/api/v1/admin/permissions/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var catalogue =
            (await AuthenticationClient.ReadAsync<PermissionBody[]>(response)).ShouldNotBeNull();

        using var scope = fixture.Services.CreateScope();
        var declared = scope.ServiceProvider.GetRequiredService<PermissionCatalogue>().All;

        catalogue.Length.ShouldBe(declared.Count, "the screen picks from the list the endpoint enforces");

        var stepUp = catalogue.First(p => p.Key == PlatformPermissions.FeatureFlags);
        stepUp.RequiresStepUp.ShouldBeTrue();
        stepUp.RequiresReason.ShouldBeTrue();
        stepUp.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task BeingSignedInIsNotEnoughToReadOrEditTheRoleRegister()
    {
        using var stranger = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-roledenied", "203.0.113.159", grantPermission: null);

        (await stranger.GetAsync("/api/v1/admin/roles/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.GetAsync("/api/v1/admin/permissions/")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await stranger.PostAsync(
                "/api/v1/admin/roles/",
                new { key = "sneaky", name = "Sneaky", description = "", reach = "Organisation", reason = Reason },
                ("Idempotency-Key", Guid.CreateVersion7().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(RoleAdministrationEndpointTests))]
    public async Task AChangeWithoutAReasonIsRefusedAndAnUnknownReachIsToo()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-rolewhy", "203.0.113.160", Permissions.Roles);

        var (roleId, version) = await CustomRoleAsync(administrator, "why", RoleReach.Branch);

        (await administrator.PutAsync(
                $"/api/v1/admin/roles/{roleId}/permissions",
                new { permissionKeys = Array.Empty<string>() },
                ("If-Match", version),
                ("Idempotency-Key", Guid.CreateVersion7().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await administrator.PostAsync(
                "/api/v1/admin/roles/",
                new
                {
                    key = $"bad_{AdministrationHarness.UniqueToken(8)}",
                    name = "Somewhere in between",
                    description = "",
                    reach = "Regional",
                    reason = Reason,
                },
                ("Idempotency-Key", Guid.CreateVersion7().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<(Guid RoleId, string Version)> CustomRoleAsync(
        AdministrationHarness.AdministratorClient administrator, string label, RoleReach reach)
    {
        var response = await administrator.PostAsync(
            "/api/v1/admin/roles/",
            new
            {
                key = $"{label}_{AdministrationHarness.UniqueToken(10)}",
                name = $"Role for {label}",
                description = "A role created for one test.",
                reach = reach.ToString(),
                reason = Reason,
            },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var role = (await AuthenticationClient.ReadAsync<RoleBody>(response)).ShouldNotBeNull();

        return (role.RoleId, response.Headers.ETag.ShouldNotBeNull().Tag);
    }

    private static async Task<RoleBody> ReadAsync(
        AdministrationHarness.AdministratorClient administrator, Guid roleId)
    {
        var response = await administrator.GetAsync($"/api/v1/admin/roles/{roleId}");

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await AuthenticationClient.ReadAsync<RoleBody>(response)).ShouldNotBeNull();
    }

    private static async Task<string> VersionOfAsync(
        AdministrationHarness.AdministratorClient administrator, Guid roleId)
    {
        var response = await administrator.GetAsync($"/api/v1/admin/roles/{roleId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return response.Headers.ETag.ShouldNotBeNull().Tag;
    }

    private async Task GrantDirectlyAsync(Guid roleId, string permissionKey)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Time.IClock>();

        var role = (await context.Roles
            .Include(candidate => candidate.Permissions)
            .SingleAsync(candidate => candidate.Id == roleId, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        role.Grant(permissionKey, clock.UtcNow, by: null).IsSuccess.ShouldBeTrue();

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> RoleOfAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.UserRoles
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => assignment.RoleId)
            .FirstAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> SystemRoleAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Time.IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<
            Tailor360.Platform.Abstractions.Identifiers.IIdGenerator>();

        // This fixture migrates the schema and seeds no reference data, so the system role a real
        // installation would already have is created here, once, by whichever test gets there first.
        var existing = await context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(role => role.IsSystem, TestContext.Current.CancellationToken);

        if (existing is not null)
        {
            return existing.Id;
        }

        var role = Role.Define(
            ids.NewId(),
            SessionTestData.OrganisationId,
            "owner",
            "Owner",
            "The owner of the business.",
            RoleReach.Organisation,
            clock.UtcNow,
            isSystem: true).Value;

        context.Roles.Add(role);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return role.Id;
    }

    private static async Task<TrailBody> ReadTrailAsync(
        AdministrationHarness.AdministratorClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/admin/audit/{query}");

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await AuthenticationClient.ReadAsync<TrailBody>(response)).ShouldNotBeNull();
    }

    private sealed record RoleBody(
        Guid RoleId,
        string Key,
        string Name,
        string Description,
        string Reach,
        bool IsSystem,
        bool AssignedByDefault,
        IReadOnlyList<string> PermissionKeys,
        int Holders,
        string Version);

    private sealed record PermissionBody(
        string Key,
        string Description,
        string Module,
        string Scope,
        bool RequiresMfa,
        bool RequiresStepUp,
        bool RequiresReason);

    private sealed record TrailBody(IReadOnlyList<TrailEntryBody> Entries, string? NextCursor);

    private sealed record TrailEntryBody(
        string Action,
        Guid? ActorId,
        string? Reason,
        string Summary,
        string? Before,
        string? After);
}
