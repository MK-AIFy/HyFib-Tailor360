using System.Net;
using System.Text.Json;
using OtpNet;
using Shouldly;
using Tailor360.IntegrationTests.Identity;

namespace Tailor360.IntegrationTests.Authorization;

/// <summary>
/// The routes that take an identifier a caller could type rather than follow, asked with somebody
/// else's identifier and with one that belongs to nobody.
/// </summary>
/// <remarks>
/// <para>
/// The rule is that the two answers are indistinguishable — same status, same code, same body. It is
/// not enough for both to be refusals: a refusal that says "that session is not yours" and a refusal
/// that says "no such session" together make the endpoint an oracle, and a few thousand requests turn
/// that oracle into a list of which identifiers are live sessions on other people's accounts.
/// </para>
/// <para>
/// The cases come from <c>matrix.yaml</c> rather than being listed here, so that a later pull request
/// adding a route with an identifier in it adds the case where the reviewers of the matrix will see
/// it. Each case names the route and why its two answers have to be the same.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class IdentifierEditingTests(WebApplicationFixture fixture)
{
    private static readonly MatrixFixtures Fixtures = MatrixFixtures.Load();

    /// <summary>Every route the fixtures list is one this suite actually asks about.</summary>
    [Fact]
    public void EveryIdentifierEditingCaseIsCovered()
    {
        string[] covered =
        [
            "DELETE /api/v1/sessions/{sessionId}",
            "DELETE /api/v1/auth/passkeys/{passkeyId}",
        ];

        Fixtures.IdentifierEditing.ShouldNotBeEmpty();
        Fixtures.IdentifierEditing.Select(entry => entry.Route).ShouldBe(covered, ignoreOrder: true);
        Fixtures.IdentifierEditing.ShouldAllBe(entry => entry.Because.Length > 40);
    }

    /// <summary>
    /// Revoking somebody else's live session is answered exactly as revoking one that never existed.
    /// </summary>
    [Fact]
    public async Task RevokingAnotherAccountsSessionIsAnsweredAsRevokingOneThatDoesNotExist()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var stranger = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "idor-victim");
        using var strangersClient = AuthenticationClient.Open(fixture, "203.0.113.71");
        (await SignInAsync(strangersClient, stranger.UserName)).EnsureSuccessStatusCode();

        var theirSessionId = await CurrentSessionIdAsync(strangersClient);

        var caller = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "idor-caller");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.72");
        (await SignInAsync(client, caller.UserName)).EnsureSuccessStatusCode();

        var foreign = await client.DeleteAsync($"/api/v1/sessions/{theirSessionId}");
        var invented = await client.DeleteAsync($"/api/v1/sessions/{Guid.CreateVersion7()}");

        foreign.StatusCode.ShouldBe(invented.StatusCode);
        (await AuthenticationClient.CodeAsync(foreign)).ShouldBe(await AuthenticationClient.CodeAsync(invented));
        (await TellingPartOf(foreign)).ShouldBe(await TellingPartOf(invented));

        // And the session it was aimed at is still there, which is the half a status code alone would
        // not have told anybody.
        (await CurrentSessionIdAsync(strangersClient)).ShouldBe(theirSessionId);
    }

    /// <summary>
    /// The same shape on the passkey route: a passkey that really belongs to somebody else, and one
    /// that belongs to nobody, answered identically — and the stranger's is still there afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves have to be real for this to mean anything. The caller signs in and answers a second
    /// factor, so the request reaches the handler instead of stopping at the endpoint's assurance level;
    /// and the foreign identifier is a passkey seeded against another account, so what is being compared
    /// is the handler's answer about somebody else's credential rather than two identical refusals of
    /// two identifiers that match nothing.
    /// </para>
    /// <para>
    /// It would pass a weaker implementation only if that implementation answered a foreign passkey and
    /// a missing one the same way, which is the rule. The handler gets there by construction — it looks
    /// the passkey up on the caller's own aggregate, where somebody else's is simply not present — and
    /// this is what holds that construction in place.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RemovingAnotherAccountsPasskeyIsAnsweredAsRemovingOneThatDoesNotExist()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var stranger = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "idor-pk-victim");
        var theirPasskeyId = await AuthenticationTestData.RegisterPasskeyAsync(
            fixture, stranger.Id, "The stranger's phone");

        var caller = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "idor-passkey");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.73");
        await SignInWithSecondFactorAsync(client, caller.UserName);

        var foreign = await client.DeleteAsync($"/api/v1/auth/passkeys/{theirPasskeyId}");
        var invented = await client.DeleteAsync($"/api/v1/auth/passkeys/{Guid.CreateVersion7()}");

        foreign.StatusCode.ShouldBe(invented.StatusCode);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthenticationClient.CodeAsync(foreign)).ShouldBe(await AuthenticationClient.CodeAsync(invented));
        (await TellingPartOf(foreign)).ShouldBe(await TellingPartOf(invented));

        // The half a status code alone would not have told anybody: the request did not remove it.
        (await AuthenticationTestData.HoldsPasskeyAsync(fixture, stranger.Id, theirPasskeyId))
            .ShouldBeTrue();
    }

    /// <summary>
    /// Signs in and satisfies a second factor, so that a request reaches a handler behind
    /// <c>RequireSatisfiedSecondFactor</c> rather than stopping at the assurance gate.
    /// </summary>
    /// <remarks>
    /// Enrolment is driven through the API — first factor, enrol, confirm with a real code from the
    /// issued secret — because the recovery codes it returns are the only way to answer the challenge
    /// afterwards without an authenticator. The sign-out and second sign-in are what produce a session
    /// that has been challenged and has answered, which is the state under test.
    /// </remarks>
    private static async Task SignInWithSecondFactorAsync(AuthenticationClient client, string identifier)
    {
        (await SignInAsync(client, identifier)).EnsureSuccessStatusCode();

        var started = await client.PostAsync("/api/v1/auth/mfa/enrol");
        started.EnsureSuccessStatusCode();

        var enrolment = await AuthenticationClient.ReadAsync<EnrolmentBody>(started);
        enrolment.ShouldNotBeNull();

        var secret = Base32Encoding.ToBytes(
            enrolment.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal));

        var code = new Totp(secret, enrolment.PeriodSeconds, totpSize: enrolment.Digits).ComputeTotp();

        var confirmed = await client.PostAsync("/api/v1/auth/mfa/enrol/confirm", new { code });
        confirmed.EnsureSuccessStatusCode();

        var sheet = await AuthenticationClient.ReadAsync<RecoveryCodesBody>(confirmed);
        sheet.ShouldNotBeNull();

        (await client.PostAsync("/api/v1/auth/logout")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SignInAsync(client, identifier)).EnsureSuccessStatusCode();

        var answered = await client.PostAsync(
            "/api/v1/auth/mfa/challenge", new { factor = "recoveryCode", code = sheet.RecoveryCodes[0] });

        answered.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> SignInAsync(AuthenticationClient client, string identifier)
        => client.PostAsync("/api/v1/auth/login", new { identifier, password = AuthenticationTestData.Password });

    private static async Task<Guid> CurrentSessionIdAsync(AuthenticationClient client)
    {
        var response = await client.GetAsync("/api/v1/sessions");
        response.EnsureSuccessStatusCode();

        var sessions = await AuthenticationClient.ReadAsync<SessionRow[]>(response);
        return sessions!.Single(session => session.IsCurrent).SessionId;
    }

    /// <summary>
    /// The part of a problem document that could tell a caller something, with the part that varies by
    /// request removed.
    /// </summary>
    /// <remarks>
    /// <c>instance</c> echoes the path, and the path holds the identifier the caller just typed;
    /// <c>correlationId</c> and <c>traceId</c> are different on every request by design. Comparing the
    /// raw bodies would therefore always differ and prove nothing. What must not differ is everything
    /// else: the type, the title, the status, the code and the sentence a person reads.
    /// </remarks>
    private static async Task<string> TellingPartOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);

        var telling = document.RootElement.EnumerateObject()
            .Where(property => property.Name is not ("instance" or "correlationId" or "traceId"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.Value}");

        return string.Join('|', telling);
    }

    private sealed record SessionRow(Guid SessionId, bool IsCurrent);

    private sealed record EnrolmentBody(string ManualEntryKey, int PeriodSeconds, int Digits);

    private sealed record RecoveryCodesBody(IReadOnlyList<string> RecoveryCodes);
}
