using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Shouldly;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Domain.Recovery;
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
        var version = await VersionOfAsync(administrator, Route(subject.Id));

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
        var version = await VersionOfAsync(administrator, Route(subject.Id));
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
        var version = await VersionOfAsync(administrator, Route(administrator.UserId));

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

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task ASuspensionIsLiftedAndTheAccountCanSignInAgain()
    {
        using var administrator = await AdministratorAsync("adm-reinstate", "203.0.113.69");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-reinstate");

        await CommandAsync(administrator, subject.Id, "suspend");

        // Suspension blocks the sign-in itself, not only the sessions that existed — so the account
        // being able to sign in again is what makes reinstatement mean anything.
        using var refused = AuthenticationClient.Open(fixture, "203.0.113.70");
        (await refused.PostAsync(
                "/api/v1/auth/login",
                new { identifier = subject.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldNotBe(HttpStatusCode.OK);

        await CommandAsync(administrator, subject.Id, "reinstate");
        (await ReloadStatusAsync(subject.Id)).ShouldBe(UserStatus.Active);

        using var allowed = AuthenticationClient.Open(fixture, "203.0.113.71");
        (await allowed.PostAsync(
                "/api/v1/auth/login",
                new { identifier = subject.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task ClosingAnAccountEndsItsSessionsAndKeepsItsHistoryResolvable()
    {
        using var administrator = await AdministratorAsync("adm-close", "203.0.113.72");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-close");

        using var device = AuthenticationClient.Open(fixture, "203.0.113.73");
        await SignInAsync(device, subject.UserName);

        await CommandAsync(administrator, subject.Id, "deactivate");

        (await ReloadStatusAsync(subject.Id)).ShouldBe(UserStatus.Deactivated);
        (await LiveSessionsAsync(subject.Id)).ShouldBe(0);

        // The acceptance criterion that is easiest to lose: the row is still there, still readable, and
        // still the thing every audit entry and every future order points at. There is no delete
        // endpoint on this surface, and this is the test that would fail if one were ever added.
        var administered = await administrator.GetAsync(Route(subject.Id));
        administered.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = await LastAuditEntryAsync(subject.Id);
        entry.ShouldNotBeNull();
        entry.Action.ShouldBe("identity.user.deactivated");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task ReopeningAClosedAccountLeavesItUnableToSignInUntilItIsInvitedAgain()
    {
        using var administrator = await AdministratorAsync("adm-reopen", "203.0.113.74");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-reopen");

        await CommandAsync(administrator, subject.Id, "deactivate");
        await CommandAsync(administrator, subject.Id, "reactivate");

        (await ReloadStatusAsync(subject.Id)).ShouldBe(
            UserStatus.Invited, "the person returning proves who they are from the beginning");

        // The password the account had before it was closed must not work. A reactivation that restored
        // a working credential would turn a departed colleague's old password into a live one.
        using var attempt = AuthenticationClient.Open(fixture, "203.0.113.75");
        (await attempt.PostAsync(
                "/api/v1/auth/login",
                new { identifier = subject.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task AnIllegalTransitionIsRefusedAndTheAccountIsUntouched()
    {
        using var administrator = await AdministratorAsync("adm-illegal", "203.0.113.76");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-illegal");

        // Reinstating an account that was never suspended, and reopening one that was never closed.
        // Both are refused by the domain; what is asserted here is that the refusal reaches the caller
        // as a conflict rather than as a silent success or a server error.
        foreach (var segment in (string[])["reinstate", "reactivate"])
        {
            var version = await VersionOfAsync(administrator, Route(subject.Id));

            var refused = await administrator.PostAsync(
                $"{Route(subject.Id)}/{segment}",
                new { reason = Reason },
                ("If-Match", version),
                ("Idempotency-Key", Guid.CreateVersion7().ToString()));

            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        (await ReloadStatusAsync(subject.Id)).ShouldBe(UserStatus.Active);
        (await AuditEntryCountAsync(subject.Id, "identity.user.reinstated")).ShouldBe(
            0, "a refused command writes no entry, or the trail records changes that never happened");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task ClearingASecondFactorEndsTheSessionsItWasProtecting()
    {
        using var administrator = await AdministratorAsync("adm-resetmfa", "203.0.113.77");

        // The subject is a full administrator in their own right, because only an account that has
        // enrolled has a second factor to clear.
        using var subject = await AdministratorAsync("sub-resetmfa", "203.0.113.78");
        (await LiveSessionsAsync(subject.UserId)).ShouldBeGreaterThan(0);
        (await MfaStateAsync(subject.UserId)).ShouldBe(nameof(MfaEnrolmentState.Enrolled));

        await CommandAsync(administrator, subject.UserId, "reset-mfa");

        // ResetRequired rather than NotEnrolled: the account is not merely without a factor, it owes
        // one, and the sign-in path is what makes that demand.
        (await MfaStateAsync(subject.UserId)).ShouldBeOneOf(
            nameof(MfaEnrolmentState.NotEnrolled), nameof(MfaEnrolmentState.ResetRequired));
        (await LiveSessionsAsync(subject.UserId)).ShouldBe(
            0, "an account whose factor somebody else cleared must not stay signed in anywhere");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task RevokingSessionsSignsEveryDeviceOutAndLeavesTheAccountAbleToReturn()
    {
        using var administrator = await AdministratorAsync("adm-revoke", "203.0.113.79");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-revoke");

        using var phone = AuthenticationClient.Open(fixture, "203.0.113.80");
        using var counter = AuthenticationClient.Open(fixture, "203.0.113.81");
        await SignInAsync(phone, subject.UserName);
        await SignInAsync(counter, subject.UserName);

        await CommandAsync(administrator, subject.Id, "revoke-sessions");

        (await LiveSessionsAsync(subject.Id)).ShouldBe(0);

        // Unlike suspension, the account is not in trouble: the person simply signs in again.
        (await ReloadStatusAsync(subject.Id)).ShouldBe(UserStatus.Active);

        using var again = AuthenticationClient.Open(fixture, "203.0.113.82");
        (await again.PostAsync(
                "/api/v1/auth/login",
                new { identifier = subject.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task ReplacingRolesSetsExactlyTheRolesGivenAndRecordsTheKeysThatChanged()
    {
        using var administrator = await AdministratorAsync("adm-roles", "203.0.113.83");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-roles");
        var (first, second) = await TwoAssignableRolesAsync();

        await PutAsync(administrator, $"{Route(subject.Id)}/roles", new { roleKeys = new[] { first }, reason = Reason });
        (await RoleKeysOfAsync(subject.Id)).ShouldBe([first]);

        // The whole set, not an addition: sending only the second role must leave only the second.
        await PutAsync(administrator, $"{Route(subject.Id)}/roles", new { roleKeys = new[] { second }, reason = Reason });
        (await RoleKeysOfAsync(subject.Id)).ShouldBe([second]);

        var entry = await LastAuditEntryAsync(subject.Id);
        entry.ShouldNotBeNull();
        entry.Action.ShouldBe("identity.user.roles-replaced");

        // Keys, not names: a renamed role must not make an old audit entry unreadable.
        entry.Before.ShouldNotBeNull().ShouldContain(first);
        entry.After.ShouldNotBeNull().ShouldContain(second);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task ARoleThatDoesNotExistIsRefusedAndNothingIsReplaced()
    {
        using var administrator = await AdministratorAsync("adm-badrole", "203.0.113.84");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-badrole");
        var (first, _) = await TwoAssignableRolesAsync();

        await PutAsync(administrator, $"{Route(subject.Id)}/roles", new { roleKeys = new[] { first }, reason = Reason });

        var refused = await PutRawAsync(
            administrator,
            $"{Route(subject.Id)}/roles",
            new { roleKeys = new[] { first, "no_such_role" }, reason = Reason });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The whole replacement is refused, not the part of it that was valid — a partial apply would
        // leave the administrator looking at a set they did not choose.
        (await RoleKeysOfAsync(subject.Id)).ShouldBe([first]);
    }

    /// <summary>
    /// Two administrators editing one person's access at the same moment: one wins, one is told.
    /// </summary>
    /// <remarks>
    /// The verification issue #25 names by hand. Neither assignment table carries a concurrency token,
    /// so what makes this contend at all is that the change touches the account row — and this test is
    /// what proves that, because with the touch removed both writers would succeed and the second would
    /// silently discard the first.
    /// </remarks>
    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task TwoAdministratorsReplacingOneAccountsRolesAtOnceProduceOneOutcome()
    {
        const int racerCount = 6;

        using var administrator = await AdministratorAsync("adm-race", "203.0.113.85");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-race");
        var (first, second) = await TwoAssignableRolesAsync();

        // Every racer reads the same version, as six browser tabs opened from one list would.
        var version = await VersionOfAsync(administrator, $"{Route(subject.Id)}/access");

        var gate = await RowGate.HoldAsync(
            DatabaseAvailability.ConnectionString!, "identity.users", subject.Id, TestContext.Current.CancellationToken);

        var racers = Enumerable.Range(0, racerCount).Select(async index =>
            await administrator.PutAsync(
                $"{Route(subject.Id)}/roles",
                new { roleKeys = new[] { index % 2 == 0 ? first : second }, reason = Reason },
                ("If-Match", version),
                ("Idempotency-Key", Guid.CreateVersion7().ToString()))).ToList();

        await gate.ReleaseWhenWaitingAsync(racerCount, TestContext.Current.CancellationToken);
        await gate.DisposeAsync();

        var outcomes = await Task.WhenAll(racers);

        outcomes.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(
            1, "one edit is applied, whatever the interleaving");

        foreach (var loser in outcomes.Where(response => response.StatusCode != HttpStatusCode.OK))
        {
            loser.StatusCode.ShouldBe(
                HttpStatusCode.Conflict, "a losing administrator is told, not silently overwritten");
        }

        // Exactly one role, and exactly one audit entry: the losers changed nothing and recorded nothing.
        (await RoleKeysOfAsync(subject.Id)).Count.ShouldBe(1);
        (await AuditEntryCountAsync(subject.Id, "identity.user.roles-replaced")).ShouldBe(1);

        foreach (var response in outcomes)
        {
            response.Dispose();
        }
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task BranchAssignmentsAreRefusedWhenTheyWouldStrandTheAccount()
    {
        using var administrator = await AdministratorAsync("adm-branches", "203.0.113.86");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-branches");
        var home = SessionTestData.HomeBranchId;

        // Two primaries, which the store's filtered unique index would refuse as a server error.
        var twoPrimaries = await PutRawAsync(
            administrator,
            $"{Route(subject.Id)}/branches",
            new
            {
                branches = new[]
                {
                    new { branchId = home, isPrimary = true },
                    new { branchId = Guid.CreateVersion7(), isPrimary = true },
                },
                reason = Reason,
            });

        twoPrimaries.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A branch that is not open at all.
        var unknownBranch = await PutRawAsync(
            administrator,
            $"{Route(subject.Id)}/branches",
            new
            {
                branches = new[] { new { branchId = Guid.CreateVersion7(), isPrimary = false } },
                reason = Reason,
            });

        unknownBranch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Leaving out the account's own default branch, which would send every screen it opens to a
        // branch it cannot act in.
        var withoutHome = await PutRawAsync(
            administrator,
            $"{Route(subject.Id)}/branches",
            new { branches = Array.Empty<object>(), reason = Reason });

        withoutHome.StatusCode.ShouldBe(HttpStatusCode.OK, "an empty set removes every assignment");

        var valid = await PutRawAsync(
            administrator,
            $"{Route(subject.Id)}/branches",
            new { branches = new[] { new { branchId = home, isPrimary = true } }, reason = Reason });

        valid.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Paging returns every account once, even when the list changes underneath the reader.
    /// </summary>
    /// <remarks>
    /// The property a keyset cursor exists for. With an offset, inviting somebody while an
    /// administrator is on page two shifts every later row by one, so one account is shown twice and
    /// another is never shown at all — and nobody notices, because both pages look plausible.
    /// </remarks>
    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task PagingTheListReturnsEveryAccountOnceEvenWhileItIsChanging()
    {
        using var administrator = await AdministratorAsync("adm-list", "203.0.113.87");

        // The test database is migrated and not dropped between runs, so the list is scoped to this
        // run's accounts by a token in their names. Paging the whole organisation would walk every
        // account every previous run left behind, which is slow and says nothing extra.
        var token = $"l{Guid.CreateVersion7():n}"[..7];

        var seeded = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            seeded.Add((await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, token)).Id);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        var added = 0;

        do
        {
            var query = $"?limit=2&q={token}"
                        + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");

            var page = await ListAsync(administrator, query);
            page.Users.Count.ShouldBeLessThanOrEqualTo(2);

            seen.AddRange(page.Users.Select(user => user.UserId));
            cursor = page.NextCursor;

            // A fresh account after the first two pages, which is exactly what breaks an offset: with
            // one, every later row shifts by one and an account is shown twice or not at all.
            if (cursor is not null && added < 2)
            {
                await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, token);
                added++;
            }
        }
        while (cursor is not null);

        seen.Distinct().Count().ShouldBe(seen.Count, "no account is returned on two pages");

        foreach (var userId in seeded)
        {
            seen.ShouldContain(userId, "an account that existed throughout is on exactly one page");
        }
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task TheListFiltersByStatusAndNeverAnswersQuestionsAboutAnAddress()
    {
        using var administrator = await AdministratorAsync("adm-filter", "203.0.113.88");
        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-filter");

        await CommandAsync(administrator, subject.Id, "suspend");

        var suspended = await ListAsync(administrator, "?status=Suspended&limit=100");
        suspended.Users.ShouldContain(user => user.UserId == subject.Id);
        suspended.Users.ShouldAllBe(user => user.Status == nameof(UserStatus.Suspended));

        var active = await ListAsync(administrator, "?status=Active&limit=100");
        active.Users.ShouldNotContain(user => user.UserId == subject.Id);

        // A status this system does not have is a field error, not an empty list that reads as "nobody
        // is in that state".
        (await administrator.GetAsync("/api/v1/admin/users/?status=Retired"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Searching by address must not confirm whether one belongs to a member of staff, and no
        // address is returned to be matched against either.
        var byAddress = await ListAsync(administrator, $"?q={Uri.EscapeDataString(subject.Email)}&limit=100");
        byAddress.Users.ShouldBeEmpty("the free-text term matches names, never contact details");

        var byName = await ListAsync(administrator, $"?q={Uri.EscapeDataString(subject.UserName)}&limit=100");
        byName.Users.ShouldContain(user => user.UserId == subject.Id);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task AnInvitedAccountCannotSignInUntilItHasSetItsOwnPassword()
    {
        using var administrator = await AdministratorAsync("adm-invite", "203.0.113.89");
        var userName = $"inv{Guid.CreateVersion7():n}"[..16];

        var created = await administrator.PostAsync(
            "/api/v1/admin/users/",
            new
            {
                userName,
                email = $"{userName}@synthetic.invalid",
                displayName = "Invited Person",
                homeBranchId = SessionTestData.HomeBranchId,
                reason = Reason,
            },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        created.Headers.Location.ShouldNotBeNull("the administrator is told where the record is");

        var payload = await AuthenticationClient.ReadAsync<StaffUserBody>(created);
        payload.ShouldNotBeNull();
        payload.Status.ShouldBe(nameof(UserStatus.Invited));

        // The account exists and has no password. An administrator who could set one would know a
        // credential belonging to somebody else, so there is nothing here to sign in with.
        using var attempt = AuthenticationClient.Open(fixture, "203.0.113.90");
        (await attempt.PostAsync(
                "/api/v1/auth/login",
                new { identifier = userName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldNotBe(HttpStatusCode.OK);

        // A single-use invitation link was issued against the account, which is what makes it reachable.
        (await OutstandingInvitationsAsync(payload.UserId)).ShouldBe(1);

        var entry = await LastAuditEntryAsync(payload.UserId, "identity.user.invited");
        entry.ShouldNotBeNull();
        entry.Reason.ShouldBe(Reason);
        entry.Before.ShouldBeNull("nothing existed before, so there is nothing to record as a prior state");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserAdministrationEndpointTests))]
    public async Task ASignInNameOrAddressAlreadyInUseIsRefusedTheSameWayForBoth()
    {
        using var administrator = await AdministratorAsync("adm-dup", "203.0.113.91");
        var existing = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-dup");

        var byName = await InviteAsync(
            administrator, existing.UserName, $"other{Guid.CreateVersion7():n}"[..14] + "@synthetic.invalid");

        var byAddress = await InviteAsync(
            administrator, $"other{Guid.CreateVersion7():n}"[..14], existing.Email);

        byName.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        byAddress.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Identical answers. Telling an administrator which of the two collided would also tell anyone
        // who reached this endpoint whether a given address already belongs to a member of staff.
        (await AuthenticationClient.CodeAsync(byName))
            .ShouldBe(await AuthenticationClient.CodeAsync(byAddress));
    }

    private static string Route(Guid userId) => $"/api/v1/admin/users/{userId}";

    private static async Task SignInAsync(AuthenticationClient client, string userName)
        => (await client.PostAsync(
                "/api/v1/auth/login", new { identifier = userName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

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

    private static Task<HttpResponseMessage> InviteAsync(
        AdministratorClient administrator, string userName, string email)
        => administrator.PostAsync(
            "/api/v1/admin/users/",
            new { userName, email, displayName = "Duplicate Candidate", reason = Reason },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

    private async Task<int> OutstandingInvitationsAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.RecoveryTokens
            .AsNoTracking()
            .CountAsync(
                token => token.UserId == userId && token.Purpose == RecoveryPurpose.Invitation,
                TestContext.Current.CancellationToken);
    }

    private static async Task<StaffPageBody> ListAsync(AdministratorClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/admin/users/{query}");
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await AuthenticationClient.ReadAsync<StaffPageBody>(response)).ShouldNotBeNull();
    }

    private static async Task<string> VersionOfAsync(AdministratorClient client, string path)
    {
        var read = await client.GetAsync(path);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        return read.Headers.ETag.ShouldNotBeNull().Tag;
    }

    /// <summary>Sends a replacement and insists it succeeded.</summary>
    private static async Task PutAsync<TBody>(AdministratorClient client, string path, TBody body)
    {
        var response = await PutRawAsync(client, path, body);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Sends a replacement against the account's current version and returns whatever came back.</summary>
    private static async Task<HttpResponseMessage> PutRawAsync<TBody>(
        AdministratorClient client, string path, TBody body)
    {
        var userId = path[(path.IndexOf("users/", StringComparison.Ordinal) + "users/".Length)..];
        userId = userId[..userId.IndexOf('/', StringComparison.Ordinal)];

        var version = await VersionOfAsync(client, $"/api/v1/admin/users/{userId}/access");

        return await client.PutAsync(
            path,
            body,
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));
    }

    private async Task<IReadOnlyList<string>> RoleKeysOfAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.UserRoles
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Join(context.Roles, assignment => assignment.RoleId, role => role.Id, (_, role) => role.Key)
            .OrderBy(key => key)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Two roles this organisation has, created if the seeder has not run here.</summary>
    private async Task<(string First, string Second)> TwoAssignableRolesAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Time.IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Identifiers.IIdGenerator>();

        var keys = new List<string>();

        foreach (var name in (string[])["assignable_one", "assignable_two"])
        {
            var existing = await context.Roles.FirstOrDefaultAsync(
                role => role.OrganisationId == SessionTestData.OrganisationId && role.Key == name,
                TestContext.Current.CancellationToken);

            if (existing is null)
            {
                context.Roles.Add(Role.Define(
                    ids.NewId(),
                    SessionTestData.OrganisationId,
                    name,
                    $"Assignable {name}",
                    "A role these tests assign and unassign.",
                    RoleReach.Branch,
                    clock.UtcNow).Value);
            }

            keys.Add(name);
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (keys[0], keys[1]);
    }

    /// <summary>Applies one administrative command and insists it succeeded.</summary>
    private static async Task CommandAsync(AdministratorClient administrator, Guid userId, string segment)
    {
        var version = await VersionOfAsync(administrator, Route(userId));

        var response = await administrator.PostAsync(
            $"{Route(userId)}/{segment}",
            new { reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private async Task<string> MfaStateAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return (await context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == userId, TestContext.Current.CancellationToken))
            .MfaEnrolment.ToString();
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

    /// <summary>
    /// The most recent entry about one account, optionally of one action.
    /// </summary>
    /// <remarks>
    /// The filter is not a convenience. An account collects entries from more than the administrative
    /// surface — a refused sign-in writes one too — so "the last entry" answers a different question
    /// from "what did this command record", and asserting on the wrong one passes or fails for reasons
    /// that have nothing to do with the test.
    /// </remarks>
    private async Task<AuditRow?> LastAuditEntryAsync(Guid entityId, string? action = null)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return await context.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.EntityId == entityId && (action == null || entry.Action == action))
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

    private sealed record StaffPageBody(IReadOnlyList<StaffSummaryBody> Users, string? NextCursor);

    private sealed record StaffSummaryBody(Guid UserId, string UserName, string Status);

    private sealed record EnrolmentBody(string ManualEntryKey, int PeriodSeconds, int Digits);

    /// <summary>An authenticated administrator, and the account they are.</summary>
    private sealed class AdministratorClient(AuthenticationClient client, Guid userId) : IDisposable
    {
        public Guid UserId { get; } = userId;

        public Task<HttpResponseMessage> GetAsync(string path) => client.GetAsync(path);

        public Task<HttpResponseMessage> PostAsync<TBody>(
            string path, TBody body, params (string Name, string Value)[] headers)
            => client.PostAsync(path, body, headers);

        public Task<HttpResponseMessage> PutAsync<TBody>(
            string path, TBody body, params (string Name, string Value)[] headers)
            => client.PutAsync(path, body, headers);

        public void Dispose() => client.Dispose();
    }
}
