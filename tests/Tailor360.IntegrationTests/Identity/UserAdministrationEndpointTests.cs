using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Shouldly;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// The first permissioned surface in the application, tested as the control it is rather than as a
/// route that happens to answer.
/// </summary>
/// <remarks>
/// Every one of these is a refusal except two. That is deliberate: an administrative endpoint is
/// defined by what it will not do, and a suite that only proved the happy path would pass with the
/// permission, the step-up, the reason, the precondition and the idempotency key all removed.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UserAdministrationEndpointTests(WebApplicationFixture fixture)
{
    private const string Reason = "Left the company; access withdrawn at the manager's request.";

    /// <summary>Whether a PostgreSQL instance was found for this run.</summary>
    public static bool Available => DatabaseAvailability.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task SuspendingAnAccountEndsItsSessionsAndRecordsWhoDidItAndWhy()
    {
        using var administrator = await AdministratorAsync("adm-suspend", "203.0.113.60");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-suspend");

        // The subject is signed in on two devices, so "revokes active sessions" is measured against
        // sessions that genuinely exist rather than against an account that had none.
        using var phone = AuthenticationClient.Open(fixture, "203.0.113.61");
        using var counter = AuthenticationClient.Open(fixture, "203.0.113.62");
        await SignInAsync(phone, subject.UserName);
        await SignInAsync(counter, subject.UserName);
        (await LiveSessionsAsync(subject.Id)).ShouldBe(2);

        var read = await administrator.GetAsync(Route(subject.Id));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = read.Headers.ETag.ShouldNotBeNull().Tag;

        var suspended = await administrator.PostAsync(
            $"{Route(subject.Id)}/suspend",
            new { reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        suspended.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await AuthenticationClient.ReadAsync<StaffUserBody>(suspended);
        payload.ShouldNotBeNull();
        payload.Status.ShouldBe(nameof(UserStatus.Suspended));

        // The rows, not the read path: a suspension that only took effect the next time somebody
        // presented a cookie would pass an assertion about the next request and still leave every
        // session live in the table.
        (await LiveSessionsAsync(subject.Id)).ShouldBe(
            0, "suspension ends the account's sessions in the request that suspends it");

        var entry = await LastAuditEntryAsync(subject.Id);
        entry.ShouldNotBeNull();
        entry.Action.ShouldBe("identity.user.suspended");
        entry.Reason.ShouldBe(Reason);
        entry.ActorId.ShouldBe(administrator.UserId);
        var before = entry.Before.ShouldNotBeNull();
        var after = entry.After.ShouldNotBeNull();
        before.ShouldContain(nameof(UserStatus.Active));
        after.ShouldContain(nameof(UserStatus.Suspended));

        // The trail is read by more people than a log line is. It says what changed, never who the
        // person is.
        foreach (var field in (string[])[before, after, entry.Summary])
        {
            field.ShouldNotContain(subject.UserName, Case.Insensitive);
            field.ShouldNotContain("synthetic.invalid", Case.Insensitive);
        }
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task ACallerWithoutThePermissionIsRefused()
    {
        // Signed in, second factor answered, freshly re-authenticated — everything but the grant. This
        // is the test that fails if the permission is ever dropped from the route.
        using var bystander = await AdministratorAsync("adm-nogrant", "203.0.113.63", grantAdminUsers: false);
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-nogrant");

        (await bystander.GetAsync(Route(subject.Id))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var refused = await bystander.PostAsync(
            $"{Route(subject.Id)}/suspend",
            new { reason = Reason },
            ("If-Match", "\"1\""),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReloadStatusAsync(subject.Id)).ShouldBe(UserStatus.Active);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task AnAdministratorWhoHasNotProvedASecondFactorIsRefused()
    {
        // The grant is held; only the second factor is missing. A permission flagged for step-up that
        // let a password-only session through would be a control that exists only in the catalogue.
        var (user, _) = await AdministratorAccountAsync("adm-nomfa", grantAdminUsers: true);
        using var client = AuthenticationClient.Open(fixture, "203.0.113.64");
        (await client.PostAsync(
                "/api/v1/auth/login", new { identifier = user.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-nomfa");

        (await client.GetAsync(Route(subject.Id))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReloadStatusAsync(subject.Id)).ShouldBe(UserStatus.Active);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task AChangeWithNoReasonIsRefusedAndNothingHappens()
    {
        using var administrator = await AdministratorAsync("adm-noreason", "203.0.113.65");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-noreason");
        var version = await VersionOfAsync(administrator, subject.Id);

        foreach (var body in (object[])[new { reason = (string?)null }, new { reason = "   " }])
        {
            var refused = await administrator.PostAsync(
                $"{Route(subject.Id)}/suspend",
                body,
                ("If-Match", version),
                ("Idempotency-Key", Guid.CreateVersion7().ToString()));

            // 400 rather than 422, which is this codebase's answer for a validation failure
            // (Problems.StatusFor). The body carries the field error, so the screen can still point at
            // the box that was left empty.
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await AuthenticationClient.CodeAsync(refused)).ShouldBe("identity.reason-required");
        }

        (await ReloadStatusAsync(subject.Id)).ShouldBe(UserStatus.Active);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task AChangeWithNoPreconditionIsRefusedAndAStaleOneIsToldTheCurrentVersion()
    {
        using var administrator = await AdministratorAsync("adm-ifmatch", "203.0.113.66");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-ifmatch");

        var missing = await administrator.PostAsync(
            $"{Route(subject.Id)}/suspend",
            new { reason = Reason },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        missing.StatusCode.ShouldBe(
            HttpStatusCode.PreconditionRequired,
            "a client that forgot the precondition is told so, not told its payload is wrong");

        var stale = await administrator.PostAsync(
            $"{Route(subject.Id)}/suspend",
            new { reason = Reason },
            ("If-Match", "\"1\""),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        stale.Headers.ETag.ShouldNotBeNull("the refusal carries the version to reload against");

        (await ReloadStatusAsync(subject.Id)).ShouldBe(UserStatus.Active);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task ARetriedCommandSuspendsOnceAndReturnsTheFirstAnswer()
    {
        using var administrator = await AdministratorAsync("adm-retry", "203.0.113.67");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-retry");
        var version = await VersionOfAsync(administrator, subject.Id);
        var key = Guid.CreateVersion7().ToString();

        var first = await administrator.PostAsync(
            $"{Route(subject.Id)}/suspend",
            new { reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", key));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The same key, the same body — a phone that lost the answer and sent it again. The second
        // request must not be a second suspension, and must not be a conflict either: the caller is
        // owed the outcome of the request they already made.
        var replay = await administrator.PostAsync(
            $"{Route(subject.Id)}/suspend",
            new { reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", key));

        replay.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await AuditEntryCountAsync(subject.Id, "identity.user.suspended"))
            .ShouldBe(1, "a replayed command is one change, so it is one entry in the trail");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task AnAdministratorCannotSuspendTheirOwnAccount()
    {
        using var administrator = await AdministratorAsync("adm-self", "203.0.113.68");
        var version = await VersionOfAsync(administrator, administrator.UserId);

        var refused = await administrator.PostAsync(
            $"{Route(administrator.UserId)}/suspend",
            new { reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        refused.StatusCode.ShouldBe(
            HttpStatusCode.Conflict,
            "suspending yourself ends your own sessions and leaves nobody able to undo it");

        (await ReloadStatusAsync(administrator.UserId)).ShouldBe(UserStatus.Active);
    }

    private static string Route(Guid userId) => $"/api/v1/admin/users/{userId}";

    private static async Task SignInAsync(AuthenticationClient client, string userName)
        => (await client.PostAsync(
                "/api/v1/auth/login", new { identifier = userName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

    private static async Task<string> VersionOfAsync(AdministratorClient client, Guid userId)
    {
        var read = await client.GetAsync(Route(userId));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return read.Headers.ETag.ShouldNotBeNull().Tag;
    }

    /// <summary>
    /// An administrator signed in, enrolled, challenged and therefore freshly re-authenticated — which
    /// is the state every one of these endpoints demands.
    /// </summary>
    private async Task<AdministratorClient> AdministratorAsync(
        string prefix,
        string clientAddress,
        bool grantAdminUsers = true)
    {
        var (user, _) = await AdministratorAccountAsync(prefix, grantAdminUsers);
        var client = AuthenticationClient.Open(fixture, clientAddress);

        await SignInAsync(client, user.UserName);

        // Enrolling and confirming answers a second factor, which is what rotates the session into one
        // that has recently proved who is holding it. There is no shortcut: a session that reached
        // step-up freshness by any other route would not be the session the endpoint sees in production.
        var started = await client.PostAsync("/api/v1/auth/mfa/enrol");
        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        var enrolment = await AuthenticationClient.ReadAsync<EnrolmentBody>(started);
        enrolment.ShouldNotBeNull();

        var secret = Base32Encoding.ToBytes(
            enrolment.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal));
        var code = new Totp(secret, enrolment.PeriodSeconds, totpSize: enrolment.Digits).ComputeTotp();

        (await client.PostAsync("/api/v1/auth/mfa/enrol/confirm", new { code }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        return new AdministratorClient(client, user.Id);
    }

    private async Task<(StaffUser User, Guid RoleId)> AdministratorAccountAsync(
        string prefix,
        bool grantAdminUsers)
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, prefix);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Time.IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Identifiers.IIdGenerator>();
        var now = clock.UtcNow;

        // Keys are lower-case letters, digits and underscores, so the prefix is folded rather than
        // used as it is written in the test name.
        var key = new string([.. prefix.Where(char.IsAsciiLetterLower)]);

        var role = Role.Define(
            ids.NewId(),
            SessionTestData.OrganisationId,
            $"{key}_role_{Guid.CreateVersion7():n}"[..30],
            $"Role {prefix}",
            "A role created for one test.",
            RoleReach.Organisation,
            now).Value;

        // Organisation reach is two facts, not one: the role is meant to see the whole organisation,
        // and its holder is granted the permission that makes that reach real. A test that granted only
        // the administrative permission would be refused for a reason that has nothing to do with what
        // it is testing.
        role.Grant(PlatformPermissions.ReadAllBranches, now, by: null).IsSuccess.ShouldBeTrue();

        if (grantAdminUsers)
        {
            role.Grant(IdentityPermissions.Users, now, by: null).IsSuccess.ShouldBeTrue();
        }

        // The branch assignment has a foreign key, and this fixture migrates the schema without seeding
        // any reference data — so the branch is created once, by whichever test gets there first.
        if (!await context.Branches.AnyAsync(
                branch => branch.Id == SessionTestData.HomeBranchId, TestContext.Current.CancellationToken))
        {
            context.Branches.Add(Branch.Open(
                SessionTestData.HomeBranchId,
                SessionTestData.OrganisationId,
                "ADMIN1",
                "Administration test branch",
                now).Value);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        context.Roles.Add(role);
        context.UserRoles.Add(UserRoleAssignment.Create(user.Id, role.Id, now));
        context.UserBranchAssignments.Add(
            UserBranchAssignment.Create(user.Id, SessionTestData.HomeBranchId, now, isPrimary: true));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (user, role.Id);
    }

    private async Task<int> LiveSessionsAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.Sessions
            .AsNoTracking()
            .CountAsync(
                session => session.UserId == userId && session.RevokedAt == null,
                TestContext.Current.CancellationToken);
    }

    private async Task<UserStatus> ReloadStatusAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return (await context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == userId, TestContext.Current.CancellationToken)).Status;
    }

    private async Task<AuditRow?> LastAuditEntryAsync(Guid entityId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return await context.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.EntityId == entityId)
            .OrderByDescending(entry => entry.OccurredAt)
            .Select(entry => new AuditRow(
                entry.Action, entry.Summary, entry.Reason, entry.Before, entry.After, entry.ActorId))
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> AuditEntryCountAsync(Guid entityId, string action)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return await context.AuditEvents
            .AsNoTracking()
            .CountAsync(
                entry => entry.EntityId == entityId && entry.Action == action,
                TestContext.Current.CancellationToken);
    }

    private sealed record AuditRow(
        string Action, string Summary, string? Reason, string? Before, string? After, Guid? ActorId);

    private sealed record StaffUserBody(Guid UserId, string Status, string Version);

    private sealed record EnrolmentBody(string ManualEntryKey, int PeriodSeconds, int Digits);

    /// <summary>An authenticated administrator, and the account they are.</summary>
    private sealed class AdministratorClient(AuthenticationClient client, Guid userId) : IDisposable
    {
        public Guid UserId { get; } = userId;

        public Task<HttpResponseMessage> GetAsync(string path) => client.GetAsync(path);

        public Task<HttpResponseMessage> PostAsync<TBody>(
            string path, TBody body, params (string Name, string Value)[] headers)
            => client.PostAsync(path, body, headers);

        public void Dispose() => client.Dispose();
    }
}
