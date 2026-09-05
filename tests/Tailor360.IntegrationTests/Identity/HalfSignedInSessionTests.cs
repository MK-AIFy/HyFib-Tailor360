using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Shouldly;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// What a session that has answered a password and nothing else can reach.
/// </summary>
/// <remarks>
/// <para>
/// This is the regression suite for a complete second-factor bypass. Every post-password endpoint used
/// to be gated on "is there a live session", and a session that had answered only the first factor is
/// one: an attacker holding a stuffed or phished password could sign in, print the account a fresh
/// sheet of recovery codes, answer the challenge with one of them, and arrive at a session with both
/// factors satisfied — destroying the holder's own sheet on the way past, so the legitimate owner was
/// locked out at the same moment. Enrolling an authenticator and registering a passkey were two more
/// routes to the same place.
/// </para>
/// <para>
/// Each test here signs in with the password alone against an account that <em>has</em> a second factor
/// and asserts that the endpoint refuses. They are written against the real host and the real database,
/// because what they are testing is the composition — the policy on the route, the ticket the store
/// builds, and the check inside the handler — and any one of those could be right in isolation while
/// the request still succeeded.
/// </para>
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class HalfSignedInSessionTests(WebApplicationFixture fixture)
{
    private const string LoginPath = "/api/v1/auth/login";
    private const string EnrolPath = "/api/v1/auth/mfa/enrol";
    private const string ConfirmPath = "/api/v1/auth/mfa/enrol/confirm";
    private const string RecoveryCodesPath = "/api/v1/auth/mfa/recovery-codes";
    private const string ChallengePath = "/api/v1/auth/mfa/challenge";
    private const string RegisterOptionsPath = "/api/v1/auth/passkeys/register/options";
    private const string RegisterPath = "/api/v1/auth/passkeys/register";
    private const string PasskeysPath = "/api/v1/auth/passkeys";
    private const string SignOutEverywherePath = "/api/v1/auth/logout-all";
    private const string SessionsPath = "/api/v1/sessions";
    private const string MePath = "/api/v1/me";

    [Fact]
    public async Task APasswordOnlySessionCannotPrintItselfASheetOfRecoveryCodes()
    {
        // The whole attack in one request. The response body of this endpoint is a working set of
        // second factors, so a caller who could reach it with a password alone would not need the
        // second factor at all.
        var (client, codes) = await EnrolledThenSignedInWithThePasswordAloneAsync(
            "half-codes", "198.51.100.40");

        using var _ = client;

        var refused = await client.PostAsync(RecoveryCodesPath);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The body says which of the three refusals this is, so a client can tell "answer your second
        // factor" from "you do not have permission" and show the right screen.
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("security.second-factor-required");

        // Nothing was printed, so the holder's own sheet still works. That is the half of the attack
        // that locked the legitimate owner out.
        (await client.PostAsync(ChallengePath, new { factor = "recoveryCode", code = codes[0] }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task APasswordOnlySessionCannotEnrolAnAuthenticatorOfItsOwn()
    {
        // The account's confirmed factor here is an authenticator, so the domain's "already enrolled"
        // check would have caught this one. It is asserted anyway because the refusal has to come from
        // the session's state rather than from the account's: an account whose only factor is a passkey
        // has nothing for that check to fire on, and used to be enrollable by anyone with the password.
        var (client, _) = await EnrolledThenSignedInWithThePasswordAloneAsync("half-enrol", "198.51.100.41");
        using var _2 = client;

        var refused = await client.PostAsync(EnrolPath);

        // 403 from the handler rather than from the route policy: the endpoint has to stay reachable
        // by a half-signed-in caller, because an account told to enrol its first factor is in exactly
        // that state. What refuses this one is the account already having a factor to lose.
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthenticationClient.CodeAsync(refused))
            .ShouldBe("identity.second-factor-not-satisfied");

        (await client.PostAsync(ConfirmPath, new { code = "000000" })).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task APasswordOnlySessionCannotRegisterAPasskeyOrListWhatIsRegistered()
    {
        // A passkey is both factors at once, so registering one against somebody else's account is the
        // whole takeover in one request. Registration is refused before the handler runs, which is why
        // this holds whether or not a relying party is configured.
        var (client, _) = await EnrolledThenSignedInWithThePasswordAloneAsync(
            "half-passkey", "198.51.100.42");

        using var _2 = client;

        (await client.PostAsync(RegisterOptionsPath)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await client.PostAsync(
            RegisterPath,
            new { ceremonyId = "not-a-ceremony", credential = new { }, label = "Attacker's key" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await client.DeleteAsync($"{PasskeysPath}/{Guid.CreateVersion7()}")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        // Listing is not credential material, but it is still not something a half-signed-in caller
        // has any use for, and the sign-in is not finished.
        (await client.GetAsync(PasskeysPath)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task APasswordOnlySessionCanOnlyFinishSigningInOrEndItself()
    {
        var (client, codes) = await EnrolledThenSignedInWithThePasswordAloneAsync(
            "half-reach", "198.51.100.43");

        using var _ = client;

        // Ending other sessions is a denial of service a caller who has proved only a password should
        // not have, and the inventory names the addresses the account is signed in from.
        (await client.PostAsync(SignOutEverywherePath)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync(SessionsPath)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // What it may reach: its own profile, so the client can paint the challenge screen, and the
        // challenge itself.
        (await client.GetAsync(MePath)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync(ChallengePath, new { factor = "recoveryCode", code = codes[0] }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // And once it has, the rest opens.
        (await client.GetAsync(SessionsPath)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync(RecoveryCodesPath)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EnrolmentAndReprintingRecoveryCodesLeaveAnAuditTrail()
    {
        // ".Audited(...)" only attaches metadata; what writes the row is the handler. These three
        // actions were declared and never written, so the exact operation the bypass abused — printing
        // somebody a fresh sheet of recovery codes — left nothing behind at all.
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "audit-mfa");
        using var client = AuthenticationClient.Open(fixture, "198.51.100.44");

        var codes = await EnrolAsync(client, user.UserName);
        (await client.PostAsync(ChallengePath, new { factor = "recoveryCode", code = codes[0] }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsync(RecoveryCodesPath)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var actions = await AuditActionsForAsync(user.Id);

        actions.ShouldContain("identity.mfa.enrolment-started");
        actions.ShouldContain("identity.mfa.enrolment-confirmed");
        actions.ShouldContain("identity.mfa.recovery-codes-issued");
    }

    [Fact]
    public async Task AskingForARecoveryLinkIsAuditedAgainstTheAccount()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "audit-recovery");
        using var client = AuthenticationClient.Open(fixture, "198.51.100.45");

        (await client.PostAsync("/api/v1/auth/recovery/request", new { email = user.Email }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await AuditActionsForAsync(user.Id)).ShouldContain("identity.recovery.requested");
    }

    [Fact]
    public async Task ASignInIsAttributedToTheAccountThatSignedInRatherThanToTheSystem()
    {
        // Nobody is authenticated when a login request arrives — the session is created inside the
        // handler — so an entry that deferred to the request's audit context recorded every sign-in
        // against "system" and left it out of the actor index, which is the one query an investigation
        // starts from.
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "audit-actor");
        using var client = AuthenticationClient.Open(fixture, "198.51.100.46");

        (await client.PostAsync(
            LoginPath, new { identifier = user.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = fixture.Services.CreateScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        var signedIn = await platform.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.EntityId == user.Id && entry.Action == "identity.sign-in.succeeded")
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        signedIn.ShouldNotBeNull();
        signedIn.ActorId.ShouldBe(user.Id);
        signedIn.ActorDisplayName.ShouldNotBe("system");
    }

    /// <summary>
    /// Enrols an authenticator, signs out, and signs back in with the password alone — which is exactly
    /// the position an attacker holding a stuffed or phished password is in.
    /// </summary>
    private async Task<(AuthenticationClient Client, string[] Codes)>
        EnrolledThenSignedInWithThePasswordAloneAsync(string prefix, string clientAddress)
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, prefix);
        var client = AuthenticationClient.Open(fixture, clientAddress);

        var codes = await EnrolAsync(client, user.UserName);

        (await client.PostAsync("/api/v1/auth/logout")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var signedIn = await client.PostAsync(
            LoginPath, new { identifier = user.UserName, password = AuthenticationTestData.Password });

        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK);

        var step = await AuthenticationClient.ReadAsync<StepBody>(signedIn);
        step.ShouldNotBeNull();
        step.Step.ShouldBe("multiFactorRequired");

        return (client, codes);
    }

    /// <summary>Enrols an authenticator through the API and returns the sheet it issues once.</summary>
    private static async Task<string[]> EnrolAsync(AuthenticationClient client, string userName)
    {
        (await client.PostAsync(
            LoginPath, new { identifier = userName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var started = await client.PostAsync(EnrolPath);
        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        var enrolment = await AuthenticationClient.ReadAsync<EnrolmentBody>(started);
        enrolment.ShouldNotBeNull();

        var secret = Base32Encoding.ToBytes(
            enrolment.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal));

        var code = new Totp(secret, enrolment.PeriodSeconds, totpSize: enrolment.Digits).ComputeTotp();

        var confirmed = await client.PostAsync(ConfirmPath, new { code });
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var sheet = await AuthenticationClient.ReadAsync<RecoveryCodesBody>(confirmed);
        sheet.ShouldNotBeNull();

        return [.. sheet.RecoveryCodes];
    }

    private async Task<IReadOnlyList<string>> AuditActionsForAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return await platform.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.EntityId == userId)
            .Select(entry => entry.Action)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private sealed record EnrolmentBody(string ManualEntryKey, int Digits, int PeriodSeconds);

    private sealed record RecoveryCodesBody(IReadOnlyList<string> RecoveryCodes);

    private sealed record StepBody(string Step);
}
