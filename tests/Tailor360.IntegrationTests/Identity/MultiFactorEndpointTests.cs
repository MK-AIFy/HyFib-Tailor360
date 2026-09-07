using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Shouldly;
using Tailor360.Modules.Identity.Infrastructure.Persistence;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// Enrolling a second factor, being challenged for it, and recovering an account without one.
/// </summary>
/// <remarks>
/// The enrolment is driven through the API rather than seeded, because the two halves of it are what
/// the test is about: a stored secret that has never produced a working code is not a second factor,
/// and the recovery codes exist in a readable form for exactly one response.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MultiFactorEndpointTests(WebApplicationFixture fixture)
{
    private const string LoginPath = "/api/v1/auth/login";
    private const string ChallengePath = "/api/v1/auth/mfa/challenge";
    private const string EnrolPath = "/api/v1/auth/mfa/enrol";
    private const string ConfirmPath = "/api/v1/auth/mfa/enrol/confirm";
    private const string MePath = "/api/v1/me";
    private const string RecoveryRequestPath = "/api/v1/auth/recovery/request";

    [Fact]
    public async Task AnEnrolledAccountIsChallengedAndAnsweringRotatesTheSession()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "mfa");
        using var client = AuthenticationClient.Open(fixture, "198.51.100.10");

        var codes = await EnrolAsync(client, user.UserName);
        codes.Length.ShouldBe(10);

        // Signing in again now owes a second factor, and the session it gets has not satisfied one.
        (await client.PostAsync(
            "/api/v1/auth/logout")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var signedIn = await client.PostAsync(
            LoginPath, new { identifier = user.UserName, password = AuthenticationTestData.Password });

        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK);

        var step = await AuthenticationClient.ReadAsync<StepBody>(signedIn);
        step.ShouldNotBeNull();
        step.Step.ShouldBe("multiFactorRequired");
        step.Factors.Authenticator.ShouldBeTrue();
        step.Factors.RecoveryCode.ShouldBeTrue();

        var half = client.SessionCookieValue;
        (await AuthenticationClient.ReadAsync<ProfileBody>(await client.GetAsync(MePath)))!
            .Security.MfaSatisfied.ShouldBeFalse();

        var answered = await client.PostAsync(
            ChallengePath, new { factor = "recoveryCode", code = codes[0] });

        answered.StatusCode.ShouldBe(HttpStatusCode.OK);

        var outcome = await AuthenticationClient.ReadAsync<ChallengeBody>(answered);
        outcome.ShouldNotBeNull();
        outcome.RemainingRecoveryCodes.ShouldBe(9);
        outcome.Session.MfaSatisfied.ShouldBeTrue();

        // The session that answered is replaced, so a ticket captured while the account was only half
        // signed in is worth nothing once the challenge is answered.
        client.SessionCookieValue.ShouldNotBe(half);
        (await client.GetWithCookieAsync(MePath, half!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await AuthenticationClient.ReadAsync<ProfileBody>(await client.GetAsync(MePath)))!
            .Security.MfaSatisfied.ShouldBeTrue();
    }

    [Fact]
    public async Task ARecoveryCodeCannotBeSpentTwice()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "mfa-replay");
        using var client = AuthenticationClient.Open(fixture, "198.51.100.11");

        var codes = await EnrolAsync(client, user.UserName);

        (await client.PostAsync("/api/v1/auth/logout")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync(
            LoginPath,
            new { identifier = user.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsync(ChallengePath, new { factor = "recoveryCode", code = codes[0] }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsync("/api/v1/auth/logout")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync(
            LoginPath,
            new { identifier = user.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var replayed = await client.PostAsync(
            ChallengePath, new { factor = "recoveryCode", code = codes[0] });

        // A code that could be spent twice is not a one-time code, and a sheet somebody photographed
        // would work for as long as the account lived.
        replayed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthenticationClient.CodeAsync(replayed)).ShouldBe("identity.mfa-code-invalid");
    }

    [Fact]
    public async Task AWrongCodeIsRefusedWithoutSayingWhichFactorWasCloser()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "mfa-wrong");
        using var client = AuthenticationClient.Open(fixture, "198.51.100.12");

        await EnrolAsync(client, user.UserName);

        (await client.PostAsync("/api/v1/auth/logout")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync(
            LoginPath,
            new { identifier = user.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var wrongAuthenticatorCode = await client.PostAsync(
            ChallengePath, new { factor = "totp", code = "000000" });

        var wrongRecoveryCode = await client.PostAsync(
            ChallengePath, new { factor = "recoveryCode", code = "AAAA-BBBB-CCCC" });

        wrongAuthenticatorCode.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrongRecoveryCode.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await AuthenticationClient.CodeAsync(wrongAuthenticatorCode)).ShouldBe("identity.mfa-code-invalid");
        (await AuthenticationClient.CodeAsync(wrongRecoveryCode)).ShouldBe("identity.mfa-code-invalid");
    }

    [Fact]
    public async Task AChallengeCannotBeAnsweredWithoutTheSessionThatEarnedIt()
    {
        using var client = AuthenticationClient.Open(fixture, "198.51.100.13");

        var response = await client.PostAsync(ChallengePath, new { factor = "totp", code = "000000" });

        // Without a first factor there is nothing to add a second factor to.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ARecoveryRequestAnswersIdenticallyForAKnownAndAnUnknownAddress()
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "recovery");

        using var known = AuthenticationClient.Open(fixture, "198.51.100.14");
        using var unknown = AuthenticationClient.Open(fixture, "198.51.100.15");

        var knownStopwatch = Stopwatch.StartNew();
        var knownResponse = await known.PostAsync(RecoveryRequestPath, new { email = user.Email });
        knownStopwatch.Stop();

        var unknownStopwatch = Stopwatch.StartNew();
        var unknownResponse = await unknown.PostAsync(
            RecoveryRequestPath, new { email = "nobody-at-all@synthetic.invalid" });
        unknownStopwatch.Stop();

        knownResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        unknownResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var first = await knownResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var second = await unknownResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Identical wording is necessary and not sufficient: the honest implementation is fast when the
        // address is unknown and slow when it is known, and that difference is a better oracle than any
        // message would have been. Both paths are therefore held to the configured floor.
        first.ShouldBe(second);
        knownStopwatch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(300));
        unknownStopwatch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(300));

        // The request really did issue a link for the account it knows, so the identical answers are
        // hiding a real difference rather than describing two identical no-ops.
        (await OutstandingRecoveryTokensAsync(user.Id)).ShouldBe(1);
    }

    /// <summary>
    /// Enrols an authenticator through the API and returns the recovery codes, which are readable in
    /// exactly one response and nowhere else.
    /// </summary>
    private static async Task<string[]> EnrolAsync(AuthenticationClient client, string userName)
    {
        (await client.PostAsync(
            LoginPath, new { identifier = userName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var started = await client.PostAsync(EnrolPath);
        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The secret is a live credential in the body of one response. Nothing may cache it.
        started.Headers.CacheControl?.NoStore.ShouldBe(true);

        var enrolment = await AuthenticationClient.ReadAsync<EnrolmentBody>(started);
        enrolment.ShouldNotBeNull();
        enrolment.OtpAuthUri.ShouldStartWith("otpauth://totp/");

        // The manual key is what a person types when they cannot scan, so a test that can read it is
        // testing the path most staff will actually use — enrolling on the phone that holds the
        // authenticator, which cannot photograph its own screen.
        var secret = Base32Encoding.ToBytes(enrolment.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal));
        var code = new Totp(secret, enrolment.PeriodSeconds, totpSize: enrolment.Digits).ComputeTotp();

        var confirmed = await client.PostAsync(ConfirmPath, new { code });
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        confirmed.Headers.CacheControl?.NoStore.ShouldBe(true);

        var sheet = await AuthenticationClient.ReadAsync<RecoveryCodesBody>(confirmed);
        sheet.ShouldNotBeNull();

        return [.. sheet.RecoveryCodes];
    }

    private async Task<int> OutstandingRecoveryTokensAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.RecoveryTokens
            .AsNoTracking()
            .CountAsync(
                token => token.UserId == userId && token.ConsumedAt == null && token.InvalidatedAt == null,
                TestContext.Current.CancellationToken);
    }

    private sealed record EnrolmentBody(
        string OtpAuthUri,
        string ManualEntryKey,
        string Issuer,
        string AccountName,
        int Digits,
        int PeriodSeconds);

    private sealed record RecoveryCodesBody(IReadOnlyList<string> RecoveryCodes);

    private sealed record StepBody(string Step, FactorsBody Factors);

    private sealed record FactorsBody(bool Authenticator, bool RecoveryCode, bool Passkey);

    private sealed record ChallengeBody(
        int RemainingRecoveryCodes,
        bool ShouldReissueRecoveryCodes,
        bool DeviceRemembered,
        SessionBody Session);

    private sealed record SessionBody(bool MfaSatisfied);

    private sealed record ProfileBody(SecurityBody Security);

    private sealed record SecurityBody(bool MfaSatisfied);
}
