using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// The authentication scheme itself, over a real database: what a request carrying each kind of cookie
/// is authenticated as, and what the response does about a cookie the server will not honour.
/// </summary>
[Collection(SessionDatabaseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SessionAuthenticationHandlerTests(SessionDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Whether a PostgreSQL instance was found, which is what the skip condition reads.</summary>
    public static bool Available => SessionDatabaseFixture.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionAuthenticationHandlerTests))]
    public async Task ARequestWithNoCookieIsSimplyNotAuthenticated()
    {
        var harness = await HarnessAsync(nameof(ARequestWithNoCookieIsSimplyNotAuthenticated));
        await using var _ = harness;

        var (result, context) = await harness.AuthenticateAsync(cookie: null);

        // NoResult, not Fail: nothing was presented, so there is nothing to refuse and no cookie to
        // clear. A first visit must not look like an attack in the logs.
        result.None.ShouldBeTrue();
        result.Succeeded.ShouldBeFalse();
        context.Response.Headers.ShouldNotContainKey(SessionAuthenticationHandler.SessionStateHeader);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionAuthenticationHandlerTests))]
    public async Task AValidCookieAuthenticatesAndFillsTheSessionContext()
    {
        var harness = await HarnessAsync(nameof(AValidCookieAuthenticatesAndFillsTheSessionContext));
        await using var _ = harness;

        var issued = await harness.SignInAsync();

        var (result, context) = await harness.AuthenticateAsync(issued.Token);

        result.Succeeded.ShouldBeTrue();

        var sessionContext = context.RequestServices.GetRequiredService<SessionContext>();
        sessionContext.Status.ShouldBe(SessionTicketStatus.Active);
        sessionContext.SessionId.ShouldBe(issued.SessionId);

        // The principal carries the account identifier and nothing else. Permissions and branches are
        // read from the ticket, so a claim cannot outlive the row it came from.
        result.Principal.ShouldNotBeNull();
        result.Principal.Identity!.IsAuthenticated.ShouldBeTrue();
        result.Principal.Claims.Count().ShouldBe(2);
    }

    /// <summary>
    /// Revocation, at the layer a request actually goes through. The same cookie is presented twice
    /// with a revocation in between and nothing else changed.
    /// </summary>
    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionAuthenticationHandlerTests))]
    public async Task ARevokedCookieIsRefusedAndClearedOnTheNextRequest()
    {
        var harness = await HarnessAsync(nameof(ARevokedCookieIsRefusedAndClearedOnTheNextRequest));
        await using var _ = harness;

        var issued = await harness.SignInAsync();
        (await harness.AuthenticateAsync(issued.Token)).Result.Succeeded.ShouldBeTrue();

        await harness.RevokeAsync(issued.SessionId, SessionEndReason.RevokedByAdministrator);

        var (result, context) = await harness.AuthenticateAsync(issued.Token);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldNotBeNull();

        // The browser is told to drop the value, so it stops presenting a ticket the server will never
        // honour again, and the client is told why so it can show "your session ended" rather than the
        // sign-in screen with no explanation.
        context.Response.Headers.SetCookie.ToString()
            .ShouldContain(SessionAuthenticationDefaults.CookieName);
        context.Response.Headers[SessionAuthenticationHandler.SessionStateHeader]
            .ToString().ShouldBe("revoked");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionAuthenticationHandlerTests))]
    public async Task AnExpiredCookieIsRefusedAndNamedAsExpired()
    {
        var harness = await HarnessAsync(nameof(AnExpiredCookieIsRefusedAndNamedAsExpired));
        await using var _ = harness;

        var issued = await harness.SignInAsync();
        harness.Clock.Advance(TimeSpan.FromHours(13));

        var (result, context) = await harness.AuthenticateAsync(issued.Token);

        result.Succeeded.ShouldBeFalse();
        context.Response.Headers[SessionAuthenticationHandler.SessionStateHeader]
            .ToString().ShouldBe("expired");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage, SkipUnless = nameof(Available), SkipType = typeof(SessionAuthenticationHandlerTests))]
    public async Task AForgedCookieValueIsRefusedAsUnknown()
    {
        var harness = await HarnessAsync(nameof(AForgedCookieValueIsRefusedAsUnknown));
        await using var _ = harness;

        await harness.SignInAsync();

        var (result, context) = await harness.AuthenticateAsync("not-a-session-anyone-issued");

        result.Succeeded.ShouldBeFalse();
        context.Response.Headers[SessionAuthenticationHandler.SessionStateHeader]
            .ToString().ShouldBe("unknown");
    }

    private async Task<SessionHandlerHarness> HarnessAsync(string name)
    {
        var connectionString = await fixture.CreateDatabaseAsync(name);
        var clock = new TestClock(Start);
        var services = SessionDatabaseFixture.BuildServices(connectionString, clock);

        await using (var seed = SessionDatabaseFixture.CreateContext(connectionString))
        {
            await SessionTestData.CreateActiveUserAsync(seed, clock.UtcNow);
        }

        return new SessionHandlerHarness(services, connectionString, clock);
    }
}

/// <summary>
/// Runs the real authentication handler against a real request. Nothing here is a substitute: the
/// handler, the ticket store, the context and the database are the ones the web host composes.
/// </summary>
/// <param name="services">The service graph.</param>
/// <param name="connectionString">The test database.</param>
/// <param name="clock">The clock the test moves.</param>
public sealed class SessionHandlerHarness(
    ServiceProvider services,
    string connectionString,
    TestClock clock) : IAsyncDisposable
{
    /// <summary>The clock the test moves.</summary>
    public TestClock Clock { get; } = clock;

    /// <summary>Signs the seeded account in and returns the cookie value.</summary>
    public async Task<IssuedSession> SignInAsync()
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Tailor360.Modules.Identity.Infrastructure.Persistence.IdentityDbContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();

        var userId = context.Users.Select(user => user.Id).First();

        return (await sessions.StartAsync(
            new StartSessionRequest(userId, "Chrome on Windows"),
            TestContext.Current.CancellationToken)).Value;
    }

    /// <summary>Revokes a session, as an administrator or another device would.</summary>
    public async Task RevokeAsync(Guid sessionId, SessionEndReason reason)
    {
        await using var scope = services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();

        await sessions.RevokeAsync(sessionId, reason, TestContext.Current.CancellationToken);
    }

    /// <summary>Runs one request through the authentication handler.</summary>
    public async Task<(AuthenticateResult Result, HttpContext Context)> AuthenticateAsync(string? cookie)
    {
        var scope = services.CreateAsyncScope();

        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("tailor360.example.com");
        context.Request.Path = "/api/v1/me";

        if (cookie is not null)
        {
            context.Request.Headers.Cookie = $"{SessionAuthenticationDefaults.CookieName}={cookie}";
        }

        var handler = new SessionAuthenticationHandler(
            new OptionsMonitorStub(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var scheme = new AuthenticationScheme(
            SessionAuthenticationDefaults.Scheme, null, typeof(SessionAuthenticationHandler));

        await handler.InitializeAsync(scheme, context);
        var result = await handler.AuthenticateAsync();

        // The scope stays alive for the assertions, which read the session context the handler filled.
        _ = connectionString;
        return (result, context);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await services.DisposeAsync();

    private sealed class OptionsMonitorStub : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();

        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
