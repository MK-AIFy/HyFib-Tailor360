using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// The sign-in surface as a browser meets it: the cookie it gets, what it is told when it is wrong,
/// what stops it guessing, and what happens to a ticket after it is revoked.
/// </summary>
/// <remarks>
/// Every test uses its own account and its own client address, so the per-account throttle, the
/// per-address throttle and the endpoint rate limits count each test separately. Sharing either would
/// make the suite order-dependent, which is the failure mode these controls are most likely to produce
/// and the least likely to be diagnosed correctly.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuthenticationEndpointTests(WebApplicationFixture fixture)
{
    private const string LoginPath = "/api/v1/auth/login";
    private const string MePath = "/api/v1/me";
    private const string LogoutPath = "/api/v1/auth/logout";
    private const string LogoutAllPath = "/api/v1/auth/logout-all";
    private const string SessionsPath = "/api/v1/sessions";

    [Fact]
    public async Task SigningInIssuesAHostPrefixedCookieAndReturnsNoTokenInTheBody()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "signin-ok");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.10");

        var response = await SignInAsync(client, user.UserName, AuthenticationTestData.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var setCookie = client.LastSetCookies
            .Single(value => value.StartsWith(SessionAuthenticationDefaults.CookieName, StringComparison.Ordinal));

        // The __Host- prefix is only honoured when all three of these hold, and it is what stops a
        // sibling host or anything over plain HTTP from planting a session.
        setCookie.ShouldContain("path=/", Case.Insensitive);
        setCookie.ShouldContain("secure", Case.Insensitive);
        setCookie.ShouldNotContain("domain=", Case.Insensitive);

        // Script must not be able to lift the session out of the browser.
        setCookie.ShouldContain("httponly", Case.Insensitive);
        setCookie.ShouldContain("samesite=lax", Case.Insensitive);

        // No Expires and no Max-Age: closing the browser drops the ticket, which is what a shared
        // counter device needs. When the session ends is decided by the server-side row.
        setCookie.ShouldNotContain("expires=", Case.Insensitive);
        setCookie.ShouldNotContain("max-age=", Case.Insensitive);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = client.SessionCookieValue;

        token.ShouldNotBeNullOrWhiteSpace();
        body.ShouldNotContain(token!, Case.Sensitive);
        body.ShouldNotContain(AuthenticationTestData.Password, Case.Sensitive);

        var payload = await AuthenticationClient.ReadAsync<SignInBody>(response);
        payload.ShouldNotBeNull();
        payload.Step.ShouldBe("complete");
        payload.UserId.ShouldBe(user.Id);
        payload.Session.WarningLeadSeconds.ShouldBe(120);
    }

    [Fact]
    public async Task AWrongPasswordAndAnUnknownAccountAreAnsweredIdentically()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "enumeration");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.11");

        var wrongPassword = await SignInAsync(client, user.UserName, "not-the-password-at-all");
        var unknownAccount = await SignInAsync(client, "nobody-by-that-name", "not-the-password-at-all");

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownAccount.StatusCode.ShouldBe(wrongPassword.StatusCode);
        unknownAccount.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var first = await wrongPassword.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var second = await unknownAccount.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Byte for byte, apart from the correlation identifier every response carries. A single field
        // that differed here would turn the sign-in form into a directory of who works in the shop.
        Without(first).ShouldBe(Without(second));
    }

    [Fact]
    public async Task AWrongPasswordAndAnUnknownAccountTakeTheSameTimeToAnswer()
    {
        // Identical wording is necessary and not sufficient. The branches do visibly different amounts
        // of work — a known account loads its whole aggregate and writes a failure count, an unknown
        // one does neither — and that difference is a better account oracle than any message would
        // have been, because the same branch answers a locked-out account and so also says who is
        // currently locked out.
        //
        // Two controls make them equal. The decoy password hash is derived once for the process, so
        // both branches cost exactly one Argon2id verification; an earlier build derived the decoy per
        // request, which made the unknown branch cost two and answer in roughly twice the time. The
        // uniform floor then covers what remains.
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "timing");

        var known = await FastestSignInAsync("203.0.113.40", user.UserName);
        var unknown = await FastestSignInAsync("203.0.113.41", "nobody-by-that-name-at-all");

        // The fastest of several samples rather than the mean, because noise only ever makes a call
        // slower: the minimum is the closest thing to the true cost of the branch.
        var difference = (known - unknown).Duration();

        difference.ShouldBeLessThan(
            TimeSpan.FromMilliseconds(150),
            $"A known account answered in {known.TotalMilliseconds:F0} ms and an unknown one in "
            + $"{unknown.TotalMilliseconds:F0} ms. A gap of tens of milliseconds is separable over a "
            + "network in a handful of samples, and it says who holds an account.");
    }

    [Fact]
    public async Task ASuspendedAccountIsRefusedTheSameWayAWrongPasswordIs()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "suspended");
        await SuspendAsync(user.Id);

        using var client = AuthenticationClient.Open(fixture, "203.0.113.12");

        var suspended = await SignInAsync(client, user.UserName, AuthenticationTestData.Password);
        var wrongPassword = await SignInAsync(client, user.UserName, "not-the-password-at-all");

        suspended.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthenticationClient.CodeAsync(suspended)).ShouldBe("identity.invalid-credentials");

        Without(await suspended.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldBe(Without(
                await wrongPassword.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task SigningInReplacesAnyTicketThePersonWasAlreadyHolding()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "fixation");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.13");

        (await SignInAsync(client, user.UserName, AuthenticationTestData.Password)).EnsureSuccessStatusCode();
        var planted = client.SessionCookieValue;

        (await SignInAsync(client, user.UserName, AuthenticationTestData.Password)).EnsureSuccessStatusCode();
        var issued = client.SessionCookieValue;

        // Session fixation, closed: the value the browser held before the sign-in is not the value it
        // holds after it, and the old one is revoked in the same unit of work.
        issued.ShouldNotBe(planted);

        using var replay = AuthenticationClient.Open(fixture, "203.0.113.13");
        var refused = await ReplayAsync(replay, planted!);

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FiveWrongPasswordsLockTheAccountWithoutSayingSo()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "lockout");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.14");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var refused = await SignInAsync(client, user.UserName, "not-the-password-at-all");
            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var locked = await AuthenticationTestData.ReloadAsync(fixture, user.Id);
        locked.ShouldNotBeNull();
        locked.FailedSignInCount.ShouldBe(5);
        locked.LockedOutUntil.ShouldNotBeNull();

        // The right password is now refused too, and refused identically — telling the caller that the
        // password was right but the account is locked would confirm the password to whoever guessed it.
        var afterLockout = await SignInAsync(client, user.UserName, AuthenticationTestData.Password);
        afterLockout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthenticationClient.CodeAsync(afterLockout)).ShouldBe("identity.invalid-credentials");
    }

    [Fact]
    public async Task TheThrottleRefusesFurtherAttemptsWithARetryAfterTheClientCanObey()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "throttled");

        // Two addresses, because the throttle and the endpoint's rate-limit policy count different
        // things and this test is about the throttle. Ten attempts from one address would be refused by
        // the policy — which is also correct, and also not what is being asserted here. The account's
        // own counter is shared across addresses, so it is what runs out first this way. That is the
        // point of having both: an attacker who spreads across addresses defeats the policy and not the
        // throttle.
        using var first = AuthenticationClient.Open(fixture, "203.0.113.15");
        using var second = AuthenticationClient.Open(fixture, "203.0.113.115");

        HttpResponseMessage? refused = null;

        for (var attempt = 0; attempt < 14 && refused is null; attempt++)
        {
            var client = attempt % 2 == 0 ? first : second;
            var response = await SignInAsync(client, user.UserName, "not-the-password-at-all");

            if (response.StatusCode is HttpStatusCode.TooManyRequests)
            {
                refused = response;
            }
        }

        refused.ShouldNotBeNull();
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("identity.too-many-attempts");

        var retryAfter = refused.Headers.RetryAfter?.Delta;
        retryAfter.ShouldNotBeNull();
        retryAfter!.Value.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task AForgedSignInFromAnotherOriginIsRefusedBeforeTheCredentialsAreRead()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "login-csrf");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.16");

        var response = await client.PostWithoutTokenAsync(
            LoginPath,
            new { identifier = user.UserName, password = AuthenticationTestData.Password });

        // Login CSRF is an attack in its own right: a forged sign-in plants the attacker's account in
        // the victim's browser, and everything the victim then does is recorded against it.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.SessionCookieValue.ShouldBeNull();
    }

    [Fact]
    public async Task SigningOutClearsTheCookieAndTheOldTicketStopsWorkingImmediately()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "signout");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.17");

        (await SignInAsync(client, user.UserName, AuthenticationTestData.Password)).EnsureSuccessStatusCode();
        var ticket = client.SessionCookieValue;

        (await client.GetAsync(MePath)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var signedOut = await client.PostAsync(LogoutPath);
        signedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        client.LastSetCookies.ShouldContain(
            value => value.StartsWith(SessionAuthenticationDefaults.CookieName, StringComparison.Ordinal));

        using var replay = AuthenticationClient.Open(fixture, "203.0.113.17");
        (await ReplayAsync(replay, ticket!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SigningOutEverywhereEndsEverySessionOnTheAccount()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "signout-all");

        using var counter = AuthenticationClient.Open(fixture, "203.0.113.18");
        using var workroom = AuthenticationClient.Open(fixture, "203.0.113.19");

        (await SignInAsync(counter, user.UserName, AuthenticationTestData.Password)).EnsureSuccessStatusCode();
        (await SignInAsync(workroom, user.UserName, AuthenticationTestData.Password)).EnsureSuccessStatusCode();

        var response = await workroom.PostAsync(LogoutAllPath);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await AuthenticationClient.ReadAsync<SignOutAllBody>(response);
        payload.ShouldNotBeNull();
        payload.SessionsEnded.ShouldBeGreaterThanOrEqualTo(2);

        // The other device is signed out on its very next request, not at its next sign-in. Revocation
        // that is only honoured at sign-in is not revocation.
        (await counter.GetAsync(MePath)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheInventoryShowsOnlyTheCallersOwnSessionsAndWillNotRevokeAnothers()
    {
        var mineUser = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "inventory-mine");
        var theirsUser = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "inventory-theirs");

        using var mine = AuthenticationClient.Open(fixture, "203.0.113.20");
        using var theirs = AuthenticationClient.Open(fixture, "203.0.113.21");

        (await SignInAsync(mine, mineUser.UserName, AuthenticationTestData.Password)).EnsureSuccessStatusCode();
        (await SignInAsync(theirs, theirsUser.UserName, AuthenticationTestData.Password)).EnsureSuccessStatusCode();

        var listed = await mine.GetAsync(SessionsPath);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var sessions = await AuthenticationClient.ReadAsync<SessionBody[]>(listed);
        sessions.ShouldNotBeNull();
        sessions.Length.ShouldBe(1);
        sessions[0].IsCurrent.ShouldBeTrue();

        var otherSessions = await AuthenticationClient.ReadAsync<SessionBody[]>(
            await theirs.GetAsync(SessionsPath));

        otherSessions.ShouldNotBeNull();

        // Naming somebody else's session identifier must not end their session, and must not confirm
        // that the identifier names anything at all.
        var refused = await mine.DeleteAsync($"{SessionsPath}/{otherSessions[0].SessionId}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("identity.session-not-active");

        (await theirs.GetAsync(MePath)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheProfileCarriesTheLocaleAndDisplayPreferencesTheShellNeeds()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "profile");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.22");

        (await SignInAsync(client, user.UserName, AuthenticationTestData.Password)).EnsureSuccessStatusCode();

        var response = await client.GetAsync(MePath);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The body carries the holder's own address and their account's security state, for this person
        // on this request. A shared counter browser must not keep a copy.
        response.Headers.CacheControl?.NoStore.ShouldBe(true);

        var profile = await AuthenticationClient.ReadAsync<ProfileBody>(response);
        profile.ShouldNotBeNull();
        profile.UserId.ShouldBe(user.Id);
        profile.Preferences.Locale.ShouldBe(UserPreferences.DefaultLocale);
        profile.Preferences.TimeZoneId.ShouldNotBeNullOrWhiteSpace();
        profile.Preferences.Theme.ShouldBe(nameof(InterfaceTheme.System));
        profile.Security.MfaSatisfied.ShouldBeFalse();
        profile.Session.AbsoluteExpiresAt.ShouldBeGreaterThan(profile.Session.IdleExpiresAt);
    }

    [Fact]
    public async Task AnUnauthenticatedRequestForTheProfileIsRefused()
    {
        using var client = AuthenticationClient.Open(fixture, "203.0.113.23");

        (await client.GetAsync(MePath)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync(SessionsPath)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ASignInIsWrittenToTheAuditTrailAgainstTheAccountThatSignedIn()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "audited");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.24");

        (await SignInAsync(client, user.UserName, AuthenticationTestData.Password)).EnsureSuccessStatusCode();

        using var scope = fixture.Services.CreateScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var entries = await platform.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.EntityId == user.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        var signedIn = entries.SingleOrDefault(entry => entry.Action == "identity.sign-in.succeeded");
        signedIn.ShouldNotBeNull();
        signedIn.EntityType.ShouldBe("StaffUser");

        // The trail is hash-chained by a database trigger, so a row that was written really was written
        // by the insert and not patched in afterwards.
        signedIn.Hash.ShouldNotBeNullOrWhiteSpace();
        signedIn.Sequence.ShouldBeGreaterThan(0);

        // Nothing readable about the credential may appear anywhere in the entry.
        var written = string.Join('|', signedIn.Summary, signedIn.After ?? string.Empty);
        written.ShouldNotContain(AuthenticationTestData.Password, Case.Sensitive);
        written.ShouldNotContain(client.SessionCookieValue!, Case.Sensitive);
    }

    [Fact]
    public async Task AFailedSignInIsAuditedAgainstTheAccountWithoutRecordingWhatWasTyped()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "audited-failure");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.25");

        await SignInAsync(client, user.UserName, "not-the-password-at-all");

        using var scope = fixture.Services.CreateScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var failure = await platform.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.EntityId == user.Id && entry.Action == "identity.sign-in.failed")
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        failure.ShouldNotBeNull();
        failure.Summary.ShouldNotContain("not-the-password-at-all", Case.Sensitive);
    }

    private static Task<HttpResponseMessage> SignInAsync(
        AuthenticationClient client,
        string identifier,
        string password)
        => client.PostAsync(LoginPath, new { identifier, password });

    /// <summary>
    /// How long the quickest of a few refused sign-ins took. Four samples, because the fifth
    /// consecutive failure on one account is a lockout and that is a different branch; the client
    /// address is the test's own, so no other test's attempts are counted against it.
    /// </summary>
    private async Task<TimeSpan> FastestSignInAsync(string clientAddress, string identifier)
    {
        using var client = AuthenticationClient.Open(fixture, clientAddress);

        // One request first, not measured: it fetches the anti-forgery pair and lets the process
        // derive its decoy hash, and neither cost belongs to the branch being timed.
        (await SignInAsync(client, identifier, "not-the-password-at-all")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);

        var fastest = TimeSpan.MaxValue;

        for (var sample = 0; sample < 3; sample++)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await SignInAsync(client, identifier, "not-the-password-at-all");
            stopwatch.Stop();

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            if (stopwatch.Elapsed < fastest)
            {
                fastest = stopwatch.Elapsed;
            }
        }

        return fastest;
    }

    /// <summary>Presents a ticket the server has revoked, exactly as a stolen cookie would.</summary>
    private static Task<HttpResponseMessage> ReplayAsync(AuthenticationClient client, string ticket)
        => client.GetWithCookieAsync(MePath, ticket);

    private async Task SuspendAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Time.IClock>();

        var user = await context.Users.FirstAsync(
            candidate => candidate.Id == userId, TestContext.Current.CancellationToken);

        user.Suspend(clock.UtcNow, by: null).IsSuccess.ShouldBeTrue();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Renders a problem-details body without the fields that differ between any two requests — the
    /// correlation, trace and request identifiers — so that everything else can be compared for being
    /// identical.
    /// </summary>
    private static string Without(string json)
    {
        string[] perRequest = ["correlationId", "traceId", "requestId"];

        using var document = System.Text.Json.JsonDocument.Parse(json);

        var remaining = document.RootElement.EnumerateObject()
            .Where(property => !perRequest.Contains(property.Name, StringComparer.Ordinal))
            .Select(property => $"{property.Name}={property.Value}")
            .OrderBy(text => text, StringComparer.Ordinal);

        return string.Join('&', remaining);
    }

    private sealed record SignInBody(
        string Step,
        Guid UserId,
        string DisplayName,
        bool MustChangePassword,
        SessionExpiryBody Session);

    private sealed record SessionExpiryBody(
        DateTimeOffset IdleExpiresAt,
        DateTimeOffset AbsoluteExpiresAt,
        int WarningLeadSeconds,
        bool MfaSatisfied);

    private sealed record SignOutAllBody(int SessionsEnded);

    private sealed record SessionBody(Guid SessionId, string DeviceLabel, bool IsCurrent);

    private sealed record ProfileBody(
        Guid UserId,
        SecurityBody Security,
        PreferencesBody Preferences,
        SessionExpiryBody Session);

    private sealed record SecurityBody(string MfaEnrolment, bool MfaSatisfied);

    private sealed record PreferencesBody(string Locale, string TimeZoneId, string Theme);
}
