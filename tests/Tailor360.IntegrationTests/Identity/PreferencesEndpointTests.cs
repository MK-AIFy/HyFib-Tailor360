using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Shouldly;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// Replacing the caller's own interface preferences through <c>PUT /api/v1/me/preferences</c>.
/// </summary>
/// <remarks>
/// Every request here goes through the real host and a real database, because what is under test is the
/// composition — the self-service assurance policy, the handler taking the account from the session, and
/// the round trip through <c>GET /api/v1/me</c> — and any one of those could be right in isolation while
/// the request still did the wrong thing.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PreferencesEndpointTests(WebApplicationFixture fixture)
{
    private const string LoginPath = "/api/v1/auth/login";
    private const string LogoutPath = "/api/v1/auth/logout";
    private const string EnrolPath = "/api/v1/auth/mfa/enrol";
    private const string ConfirmPath = "/api/v1/auth/mfa/enrol/confirm";
    private const string MePath = "/api/v1/me";
    private const string PreferencesPath = "/api/v1/me/preferences";

    public static bool Available => DatabaseAvailability.IsAvailable;

    [Fact]
    public async Task ARoundTripReturnsExactlyWhatWasSentAndTheNextReadAgrees()
    {
        Assert.SkipUnless(Available, DatabaseAvailability.SkipReason);

        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "prefs-rt");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.170");

        (await SignInAsync(client, user.UserName)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var written = await client.PutAsync(PreferencesPath, ValidBody());
        written.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await AuthenticationClient.ReadAsync<PreferencesPayload>(written);
        payload.ShouldNotBeNull();
        payload.Locale.ShouldBe("ta-IN");
        payload.TimeZoneId.ShouldBe("Asia/Kolkata");
        payload.Theme.ShouldBe("Dark");
        payload.TextSize.ShouldBe("Large");
        payload.Density.ShouldBe("Compact");
        payload.ReducedMotion.ShouldBeTrue();
        payload.LandingRoute.ShouldBe("/orders/workboard");

        var me = await client.GetAsync(MePath);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);

        var profile = await AuthenticationClient.ReadAsync<CurrentUserResponse>(me);
        profile.ShouldNotBeNull();
        profile.Preferences.ShouldBe(payload);
    }

    [Fact]
    public async Task ChangingPreferencesIsAuditedByActorAndResourceOnlyAndCarriesNoValue()
    {
        Assert.SkipUnless(Available, DatabaseAvailability.SkipReason);

        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "prefs-audit");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.171");
        (await SignInAsync(client, user.UserName)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PutAsync(PreferencesPath, ValidBody())).StatusCode.ShouldBe(HttpStatusCode.OK);

        var entry = (await AuditEntriesAsync(user.Id))
            .Single(row => row.Action == "identity.preferences.changed");

        entry.EntityType.ShouldBe("StaffUser");

        // The row proves who changed their preferences and that they did, never what they changed
        // them to — the values live in the domain table, not in a second copy on the trail.
        entry.Summary.ShouldNotContain("Dark");
        entry.Summary.ShouldNotContain("ta-IN");
        entry.Summary.ShouldNotContain("Large");
    }

    [Fact]
    public async Task AnAccountWithNoPreferenceRowGetsOneCreatedAndASecondIdenticalPutIsIdempotent()
    {
        Assert.SkipUnless(Available, DatabaseAvailability.SkipReason);

        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "prefs-missing");
        await RemovePreferencesRowAsync(user.Id);

        using var client = AuthenticationClient.Open(fixture, "203.0.113.172");
        (await SignInAsync(client, user.UserName)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = ValidBody();

        var first = await client.PutAsync(PreferencesPath, body);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstPayload = await AuthenticationClient.ReadAsync<PreferencesPayload>(first);

        var second = await client.PutAsync(PreferencesPath, body);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondPayload = await AuthenticationClient.ReadAsync<PreferencesPayload>(second);

        secondPayload.ShouldBe(firstPayload);
    }

    [Fact]
    public async Task ARequestWithNoSessionAtAllIsRefused()
    {
        Assert.SkipUnless(Available, DatabaseAvailability.SkipReason);

        using var client = AuthenticationClient.Open(fixture, "203.0.113.173");

        var refused = await client.PutAsync(PreferencesPath, ValidBody());

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("security.authentication-required");
    }

    [Fact]
    public async Task ASessionThatHasNotFinishedSigningInIsRefused()
    {
        Assert.SkipUnless(Available, DatabaseAvailability.SkipReason);

        using var client = await SignedInWithThePasswordAloneAsync("prefs-half", "203.0.113.174");

        var refused = await client.PutAsync(PreferencesPath, ValidBody());

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("security.sign-in-incomplete");
    }

    public static IEnumerable<object[]> InvalidFieldCases()
    {
        yield return ["theme", "Sepia", "theme"];
        yield return ["textSize", "200", "textSize"];
        yield return ["textSize", "1", "textSize"];
        yield return ["locale", "fr-FR", "locale"];
        yield return ["timeZoneId", "Mars/Olympus", "timeZoneId"];
        yield return ["landingRoute", "https://example.test/x", "landingRoute"];
        yield return ["landingRoute", "//example.test", "landingRoute"];
    }

    [Theory]
    [MemberData(nameof(InvalidFieldCases))]
    public async Task AnInvalidFieldIsRefusedWithTheFieldNamedAndNothingChanges(
        string field, string invalidValue, string expectedField)
    {
        Assert.SkipUnless(Available, DatabaseAvailability.SkipReason);

        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "prefs-bad");
        using var client = AuthenticationClient.Open(fixture, "203.0.113.175");
        (await SignInAsync(client, user.UserName)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = ValidBody();
        body[field] = invalidValue;

        var refused = await client.PutAsync(PreferencesPath, body);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var problem = JsonDocument.Parse(
            await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problem.RootElement.GetProperty("errors").TryGetProperty(expectedField, out _).ShouldBeTrue();

        // Refused before anything was written: the account still carries its untouched defaults.
        var reloaded = await ReloadWithPreferencesAsync(user.Id);
        reloaded.ShouldNotBeNull();
        reloaded.Theme.ShouldBe(InterfaceTheme.System);
        reloaded.TextSize.ShouldBe(InterfaceTextSize.Standard);
        reloaded.Density.ShouldBe(InterfaceDensity.Comfortable);
    }

    [Fact]
    public async Task AForeignUserIdInTheBodyIsIgnoredAndOnlyTheCallersOwnRowChanges()
    {
        Assert.SkipUnless(Available, DatabaseAvailability.SkipReason);

        var caller = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "prefs-self");
        var other = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "prefs-other");

        using var client = AuthenticationClient.Open(fixture, "203.0.113.176");
        (await SignInAsync(client, caller.UserName)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = ValidBody();
        body["userId"] = other.Id.ToString();

        (await client.PutAsync(PreferencesPath, body)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var reloadedCaller = await ReloadWithPreferencesAsync(caller.Id);
        reloadedCaller.ShouldNotBeNull();
        reloadedCaller.Theme.ShouldBe(InterfaceTheme.Dark);

        var reloadedOther = await ReloadWithPreferencesAsync(other.Id);
        reloadedOther.ShouldNotBeNull();
        reloadedOther.Theme.ShouldBe(InterfaceTheme.System);
    }

    private static Dictionary<string, object?> ValidBody() => new(StringComparer.Ordinal)
    {
        ["locale"] = "ta-IN",
        ["timeZoneId"] = "Asia/Kolkata",
        ["theme"] = "Dark",
        ["textSize"] = "Large",
        ["density"] = "Compact",
        ["reducedMotion"] = true,
        ["landingRoute"] = "/orders/workboard",
    };

    private static Task<HttpResponseMessage> SignInAsync(AuthenticationClient client, string userName)
        => client.PostAsync(LoginPath, new { identifier = userName, password = AuthenticationTestData.Password });

    /// <summary>
    /// Enrols an authenticator, then signs out and back in with the password alone, so the returned
    /// session has answered the first factor and nothing else — the state the challenge screen is
    /// painted in, and the one <c>PUT /api/v1/me/preferences</c> must refuse.
    /// </summary>
    private async Task<AuthenticationClient> SignedInWithThePasswordAloneAsync(string prefix, string clientAddress)
    {
        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, prefix);
        var client = AuthenticationClient.Open(fixture, clientAddress);

        (await SignInAsync(client, user.UserName)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var started = await client.PostAsync(EnrolPath);
        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        var enrolment = await AuthenticationClient.ReadAsync<EnrolmentBody>(started);
        enrolment.ShouldNotBeNull();

        var secret = Base32Encoding.ToBytes(
            enrolment.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal));
        var code = new Totp(secret, enrolment.PeriodSeconds, totpSize: enrolment.Digits).ComputeTotp();

        (await client.PostAsync(ConfirmPath, new { code })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync(LogoutPath)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SignInAsync(client, user.UserName)).StatusCode.ShouldBe(HttpStatusCode.OK);

        return client;
    }

    private async Task RemovePreferencesRowAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var preferences = await context.Set<UserPreferences>()
            .SingleAsync(candidate => candidate.UserId == userId, TestContext.Current.CancellationToken);

        context.Remove(preferences);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Re-reads an account's preferences directly, including the navigation
    /// <see cref="AuthenticationTestData.ReloadAsync"/> does not load.
    /// </summary>
    private async Task<UserPreferences?> ReloadWithPreferencesAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.Set<UserPreferences>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId, TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<AuditRow>> AuditEntriesAsync(Guid userId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return
        [
            .. await context.AuditEvents
                .Where(entry => entry.EntityId == userId)
                .OrderBy(entry => entry.Sequence)
                .Select(entry => new AuditRow(entry.Action, entry.EntityType, entry.Summary))
                .ToListAsync(TestContext.Current.CancellationToken),
        ];
    }

    private sealed record EnrolmentBody(string ManualEntryKey, int PeriodSeconds, int Digits);

    private sealed record AuditRow(string Action, string EntityType, string Summary);
}
