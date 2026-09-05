using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Modules.Identity.Infrastructure.Sessions;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// The session model against a real database: fixation, revocation, expiry and rotation. Every one of
/// these is a control rather than a feature, so each is asserted as a refusal, not as an absence.
/// </summary>
[Collection(SessionDatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SessionLifecycleTests(SessionDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Session fixation. The ticket the caller held before they proved who they are must not be the
    /// ticket they hold afterwards, and it must stop working — otherwise an attacker who planted a
    /// known value in someone's browser is signed in as them the moment they sign in.
    /// </summary>
    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task TheSessionIdentifierChangesWhenTheHolderAuthenticates()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(TheSessionIdentifierChangesWhenTheHolderAuthenticates));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);

        // A ticket exists before the sign-in. In the attack it was planted by someone else; here it is
        // simply the anonymous visit that preceded authenticating, which is the same shape.
        var planted = (await StartAsync(services, user.Id)).Value;

        var afterSignIn = (await StartAsync(services, user.Id, supersedes: planted.SessionId)).Value;

        afterSignIn.SessionId.ShouldNotBe(planted.SessionId);
        afterSignIn.Token.ShouldNotBe(planted.Token);

        // The identifier changing is not enough on its own: the old value has to stop resolving, or the
        // attacker still holds a live session alongside the new one.
        (await ResolveAsync(services, planted.Token)).Status.ShouldBe(SessionTicketStatus.Revoked);
        (await ResolveAsync(services, afterSignIn.Token)).Status.ShouldBe(SessionTicketStatus.Active);
    }

    /// <summary>
    /// Revocation is honoured on the next request, not at the next sign-in. This is the promise "sign
    /// out everywhere" and "suspend this account" both rest on.
    /// </summary>
    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task ARevokedSessionIsRefusedOnTheVeryNextRequest()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(ARevokedSessionIsRefusedOnTheVeryNextRequest));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);
        var issued = (await StartAsync(services, user.Id)).Value;

        (await ResolveAsync(services, issued.Token)).Status.ShouldBe(SessionTicketStatus.Active);

        await using (var scope = services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
            (await sessions.RevokeAsync(
                issued.SessionId,
                SessionEndReason.SignedOutEverywhere,
                TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        }

        // No clock movement between the revocation and the request: the refusal is immediate, and does
        // not wait for the ticket to expire on its own.
        (await ResolveAsync(services, issued.Token)).Status.ShouldBe(SessionTicketStatus.Revoked);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task SigningOutEverywhereEndsTheOtherDevicesAndSparesThisOne()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(SigningOutEverywhereEndsTheOtherDevicesAndSparesThisOne));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);

        var counter = (await StartAsync(services, user.Id, label: "Counter tablet")).Value;
        var phone = (await StartAsync(services, user.Id, label: "Phone")).Value;
        var laptop = (await StartAsync(services, user.Id, label: "Laptop")).Value;

        await using (var scope = services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var revoked = await sessions.RevokeAllForUserAsync(
                user.Id,
                SessionEndReason.SignedOutEverywhere,
                exceptSessionId: laptop.SessionId,
                TestContext.Current.CancellationToken);

            revoked.Value.ShouldBe(2);
        }

        (await ResolveAsync(services, counter.Token)).Status.ShouldBe(SessionTicketStatus.Revoked);
        (await ResolveAsync(services, phone.Token)).Status.ShouldBe(SessionTicketStatus.Revoked);
        (await ResolveAsync(services, laptop.Token)).Status.ShouldBe(SessionTicketStatus.Active);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task AnUnknownCookieValueNamesNoSession()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(AnUnknownCookieValueNamesNoSession));
        await using var _ = services;
        await using var __ = context;

        await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);

        var resolution = await ResolveAsync(services, SessionTokenFactory.CreateToken());

        resolution.Status.ShouldBe(SessionTicketStatus.Unknown);
        resolution.Ticket.ShouldBeNull();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task ASessionLeftUnusedPastTheInactivityTimeoutIsRefused()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(ASessionLeftUnusedPastTheInactivityTimeoutIsRefused));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);
        var issued = (await StartAsync(services, user.Id)).Value;

        clock.Advance(TimeSpan.FromMinutes(31));

        (await ResolveAsync(services, issued.Token)).Status.ShouldBe(SessionTicketStatus.Expired);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task WorkingSteadilySlidesTheDeadlineButNotPastTheAbsoluteLifetime()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(WorkingSteadilySlidesTheDeadlineButNotPastTheAbsoluteLifetime));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);
        var issued = (await StartAsync(services, user.Id)).Value;

        // A request every twenty-five minutes, all day: a counter tablet in use. The inactivity timeout
        // never fires, which is the point of sliding it — and the absolute lifetime still ends the
        // session inside twelve hours, which is the point of having one.
        for (var i = 0; i < 40; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(25));

            var resolution = await ResolveAsync(services, issued.Token);
            if (resolution.Status != SessionTicketStatus.Active)
            {
                // The absolute lifetime is twelve hours, so the session must end within it however
                // busy it is. Reaching here later than that would mean the cap does nothing.
                (clock.UtcNow - Start).ShouldBeGreaterThan(TimeSpan.FromHours(12));
                resolution.Status.ShouldBe(SessionTicketStatus.Expired);
                return;
            }
        }

        Assert.Fail("The session outlived its absolute lifetime, so the twelve-hour cap does nothing.");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task RotationKeepsTheOriginalAbsoluteExpiryAndRevokesThePredecessor()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(RotationKeepsTheOriginalAbsoluteExpiryAndRevokesThePredecessor));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);
        var first = (await StartAsync(services, user.Id)).Value;

        clock.Advance(TimeSpan.FromMinutes(5));

        IssuedSession second;
        await using (var scope = services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
            second = (await sessions.RotateAsync(
                first.SessionId,
                SessionRotationReason.MultiFactorSatisfied,
                TestContext.Current.CancellationToken)).Value;
        }

        // Rotating at every challenge would otherwise hand out a fresh twelve hours each time and the
        // absolute cap would quietly mean nothing.
        second.AbsoluteExpiresAt.ShouldBe(first.AbsoluteExpiresAt);
        second.Token.ShouldNotBe(first.Token);
        second.MfaSatisfied.ShouldBeTrue();

        (await ResolveAsync(services, first.Token)).Status.ShouldBe(SessionTicketStatus.Revoked);

        var ticket = (await ResolveAsync(services, second.Token)).Ticket.ShouldNotBeNull();
        ticket.MfaSatisfied.ShouldBeTrue();
        ticket.LastStrongAuthenticationAt.ShouldBe(clock.UtcNow);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task ARotationThatIsNotAStrongFactorLeavesTheStepUpWindowAlone()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(ARotationThatIsNotAStrongFactorLeavesTheStepUpWindowAlone));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);
        var first = (await StartAsync(services, user.Id)).Value;

        IssuedSession second;
        await using (var scope = services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();

            // A password change is re-authentication, but it is not multi-factor re-authentication.
            // Treating it as one would let anyone who found an unattended signed-in session approve a
            // financially final action simply by changing the password.
            second = (await sessions.RotateAsync(
                first.SessionId,
                SessionRotationReason.PasswordChanged,
                TestContext.Current.CancellationToken)).Value;
        }

        var ticket = (await ResolveAsync(services, second.Token)).Ticket.ShouldNotBeNull();
        ticket.LastStrongAuthenticationAt.ShouldBeNull();
        ticket.MfaSatisfied.ShouldBeFalse();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task ARememberedDeviceSkipsTheChallengeWithoutPassingIt()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(ARememberedDeviceSkipsTheChallengeWithoutPassingIt));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);

        IssuedSession issued;
        await using (var scope = services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
            issued = (await sessions.StartAsync(
                new StartSessionRequest(
                    user.Id,
                    "Counter tablet",
                    MfaSatisfied: true,
                    TrustedDeviceId: Guid.CreateVersion7()),
                TestContext.Current.CancellationToken)).Value;
        }

        // Remembering a device skips the challenge; it does not pass it. A session started this way
        // must not reach anything the multi-factor flag protects.
        issued.MfaSatisfied.ShouldBeFalse();

        var ticket = (await ResolveAsync(services, issued.Token)).Ticket.ShouldNotBeNull();
        ticket.MfaSatisfied.ShouldBeFalse();
        ticket.LastStrongAuthenticationAt.ShouldBeNull();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task ASessionOnASuspendedAccountIsRefusedAndEnded()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(ASessionOnASuspendedAccountIsRefusedAndEnded));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);
        var issued = (await StartAsync(services, user.Id)).Value;

        user.Suspend(clock.UtcNow, by: null).IsSuccess.ShouldBeTrue();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await ResolveAsync(services, issued.Token)).Status.ShouldBe(SessionTicketStatus.Revoked);

        // The refusal is not merely reported: the row is ended, so the repair happens once rather than
        // on every subsequent request.
        await using var reader = SessionDatabaseFixture.CreateContext(context.Database.GetConnectionString()!);
        var stored = await reader.Sessions.SingleAsync(
            session => session.Id == issued.SessionId, TestContext.Current.CancellationToken);

        stored.RevokedAt.ShouldNotBeNull();
        stored.EndReason.ShouldBe(SessionEndReason.AccountClosed);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task ADeactivatedAccountCannotStartASessionAtAll()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(ADeactivatedAccountCannotStartASessionAtAll));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);
        user.Deactivate(clock.UtcNow, by: null).IsSuccess.ShouldBeTrue();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await StartAsync(services, user.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("identity.user-deactivated");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task OnlyTheDigestOfTheCookieValueIsEverStored()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(OnlyTheDigestOfTheCookieValueIsEverStored));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);
        var issued = (await StartAsync(services, user.Id)).Value;

        await using var reader = SessionDatabaseFixture.CreateContext(context.Database.GetConnectionString()!);
        var stored = await reader.Sessions.SingleAsync(
            session => session.Id == issued.SessionId, TestContext.Current.CancellationToken);

        // A stolen database dump must yield no usable tickets.
        stored.TokenHash.ShouldNotBe(issued.Token);
        stored.TokenHash.ShouldBe(SessionTokenFactory.Digest(issued.Token));
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionLifecycleTests))]
    public async Task TheInventoryListsLiveSessionsAndMarksTheCurrentDevice()
    {
        var (services, context, clock) = await ArrangeAsync(nameof(TheInventoryListsLiveSessionsAndMarksTheCurrentDevice));
        await using var _ = services;
        await using var __ = context;

        var user = await SessionTestData.CreateActiveUserAsync(context, clock.UtcNow);
        var phone = (await StartAsync(services, user.Id, label: "Phone")).Value;
        var laptop = (await StartAsync(services, user.Id, label: "Laptop")).Value;

        await using (var scope = services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
            await sessions.RevokeAsync(
                phone.SessionId, SessionEndReason.RevokedByHolder, TestContext.Current.CancellationToken);
        }

        await using var listScope = services.CreateAsyncScope();
        var inventory = await listScope.ServiceProvider
            .GetRequiredService<ISessionService>()
            .ListForUserAsync(user.Id, laptop.SessionId, TestContext.Current.CancellationToken);

        inventory.Count.ShouldBe(1);
        inventory[0].SessionId.ShouldBe(laptop.SessionId);
        inventory[0].DeviceLabel.ShouldBe("Laptop");
        inventory[0].IsCurrent.ShouldBeTrue();
    }

    /// <summary>Whether a PostgreSQL instance was found, which is what the skip condition reads.</summary>
    public static bool Available => SessionDatabaseFixture.IsAvailable;

    private async Task<(ServiceProvider Services, IdentityDbContext Context, TestClock Clock)> ArrangeAsync(
        string name)
    {
        var connectionString = await fixture.CreateDatabaseAsync(name);
        var clock = new TestClock(Start);

        return (
            SessionDatabaseFixture.BuildServices(connectionString, clock),
            SessionDatabaseFixture.CreateContext(connectionString),
            clock);
    }

    private static async Task<Tailor360.Platform.Abstractions.Results.Result<IssuedSession>> StartAsync(
        IServiceProvider services,
        Guid userId,
        Guid? supersedes = null,
        string label = "Chrome on Windows")
    {
        await using var scope = services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();

        return await sessions.StartAsync(
            new StartSessionRequest(userId, label, SupersedesSessionId: supersedes),
            TestContext.Current.CancellationToken);
    }

    private static async Task<SessionResolution> ResolveAsync(IServiceProvider services, string token)
    {
        // A fresh scope per call, because that is what a request is. Reusing one would let a change
        // tracker hold a session in memory and hide exactly the staleness these tests are about.
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISessionTicketStore>();

        return await store.ResolveAsync(token, TestContext.Current.CancellationToken);
    }
}
