using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Security.Antiforgery;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// A browser, near enough: it fetches an anti-forgery pair, carries cookies by hand, and reports the
/// client address the test chose for it.
/// </summary>
/// <remarks>
/// <para>
/// Cookies are managed by hand rather than through a <see cref="System.Net.CookieContainer"/> because
/// every cookie under test is <c>Secure</c> and the test host answers on plain-HTTP loopback, where a
/// container silently drops them. Managing them here also means the tests can assert on the exact
/// <c>Set-Cookie</c> a real browser would receive.
/// </para>
/// <para>
/// Each instance sends its own client address, so one test's failed sign-ins cannot exhaust the
/// rate-limit or throttle budget another test is relying on.
/// </para>
/// </remarks>
public sealed class AuthenticationClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
    private readonly string _clientAddress;

    private string? _antiforgeryToken;

    private AuthenticationClient(HttpClient client, string clientAddress)
    {
        _client = client;
        _clientAddress = clientAddress;
    }

    /// <summary>How the JSON in these tests is read, matching the host's own web defaults.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Opens a client with its own client address.</summary>
    public static AuthenticationClient Open(WebApplicationFixture fixture, string clientAddress)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,

            // https, because the session and anti-forgery cookies are Secure and the framework refuses
            // to issue a Secure cookie over a plain request rather than issuing one the browser would
            // silently discard. A test that ran over http would be testing a deployment that cannot
            // exist.
            BaseAddress = new Uri("https://localhost"),
        });

        return new AuthenticationClient(client, clientAddress);
    }

    /// <summary>The session cookie value the server last issued, or null once it was cleared.</summary>
    public string? SessionCookieValue
        => _cookies.TryGetValue(SessionAuthenticationDefaults.CookieName, out var value)
            && value.Length > 0
                ? value
                : null;

    /// <summary>The raw <c>Set-Cookie</c> headers of the last response, for asserting attributes.</summary>
    public IReadOnlyList<string> LastSetCookies { get; private set; } = [];

    /// <summary>Sends a state-changing request, fetching an anti-forgery pair first if needed.</summary>
    public async Task<HttpResponseMessage> PostAsync<TBody>(string path, TBody body)
    {
        await EnsureAntiforgeryAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };

        return await SendAsync(request);
    }

    /// <summary>Sends a state-changing request with no body.</summary>
    public async Task<HttpResponseMessage> PostAsync(string path)
    {
        await EnsureAntiforgeryAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        return await SendAsync(request);
    }

    /// <summary>Sends a delete.</summary>
    public async Task<HttpResponseMessage> DeleteAsync(string path)
    {
        await EnsureAntiforgeryAsync();

        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        return await SendAsync(request);
    }

    /// <summary>
    /// Sends a read presenting one specific session cookie, whatever this client currently holds. It
    /// is how a test replays a ticket the server has revoked, exactly as a stolen cookie would.
    /// </summary>
    public async Task<HttpResponseMessage> GetWithCookieAsync(string path, string sessionCookieValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestClientAddressStartupFilter.HeaderName, _clientAddress);
        request.Headers.Add(
            "Cookie", $"{SessionAuthenticationDefaults.CookieName}={sessionCookieValue}");

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Sends a read.</summary>
    public async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(request);
    }

    /// <summary>Sends a request without the anti-forgery header, as a forged form would.</summary>
    public async Task<HttpResponseMessage> PostWithoutTokenAsync<TBody>(string path, TBody body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };

        request.Headers.Add(TestClientAddressStartupFilter.HeaderName, _clientAddress);
        request.Headers.Add("Origin", "https://localhost");

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Reads a JSON body, through the string rather than the stream so that the same response can be
    /// read more than once. The client itself inspects every refusal to decide whether to refetch an
    /// anti-forgery token, so a body that could only be read once would be empty by the time a test
    /// looked at it.
    /// </summary>
    public static async Task<TPayload?> ReadAsync<TPayload>(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return string.IsNullOrWhiteSpace(body)
            ? default
            : JsonSerializer.Deserialize<TPayload>(body, Json);
    }

    /// <summary>Reads the stable error code out of a problem-details body.</summary>
    public static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = await ReadAsync<ProblemBody>(response);
        return problem?.Code;
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private async Task EnsureAntiforgeryAsync()
    {
        if (_antiforgeryToken is not null)
        {
            return;
        }

        var response = await GetAsync("/api/v1/antiforgery");
        response.EnsureSuccessStatusCode();

        var payload = await ReadAsync<TokenBody>(response);
        _antiforgeryToken = payload?.Token;
        _antiforgeryToken.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Sends the request, and — exactly as the real client does — fetches a new anti-forgery pair and
    /// retries once when the server says the one in hand is no longer valid. The request token is bound
    /// to the signed-in account, so signing in, signing out and rotating a session all invalidate it.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var response = await SendOnceAsync(request);

        if (response.StatusCode is not System.Net.HttpStatusCode.Forbidden
            || await CodeAsync(response) is not AntiforgeryEnforcementMiddleware.ErrorCode)
        {
            return response;
        }

        response.Dispose();
        _antiforgeryToken = null;
        await EnsureAntiforgeryAsync();

        using var retry = Clone(request);
        return await SendOnceAsync(retry);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = request.Content,
        };

        foreach (var header in request.Headers.Where(header =>
                     !string.Equals(header.Key, AntiforgeryDefaults.HeaderName, StringComparison.Ordinal)
                     && !string.Equals(header.Key, "Cookie", StringComparison.Ordinal)))
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request)
    {
        request.Headers.Add(TestClientAddressStartupFilter.HeaderName, _clientAddress);

        // A real browser sends this on every same-site request, and the origin check refuses a
        // state-changing request that arrives without one or with a foreign one.
        request.Headers.Add("Origin", "https://localhost");

        if (_cookies.Count > 0)
        {
            request.Headers.Add(
                "Cookie",
                string.Join("; ", _cookies.Where(pair => pair.Value.Length > 0)
                    .Select(pair => $"{pair.Key}={pair.Value}")));
        }

        if (_antiforgeryToken is { } token)
        {
            request.Headers.Add(AntiforgeryDefaults.HeaderName, token);
        }

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Capture(response);

        return response;
    }

    private void Capture(HttpResponseMessage response)
    {
        var sessionChanged = false;

        LastSetCookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];

        foreach (var header in LastSetCookies)
        {
            var pair = header.Split(';', 2)[0];
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var name = pair[..separator];
            _cookies[name] = pair[(separator + 1)..];

            sessionChanged |= string.Equals(
                name, SessionAuthenticationDefaults.CookieName, StringComparison.Ordinal);
        }

        // The request token is bound to the signed-in account, so a response that starts, replaces or
        // ends a session invalidates the one in hand. The real client refetches at exactly these
        // moments, and a test client that did not would be testing a browser nobody ships.
        if (sessionChanged)
        {
            _antiforgeryToken = null;
        }
    }

    private sealed record TokenBody(string Token, string HeaderName);

    private sealed record ProblemBody
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("status")]
        public int Status { get; init; }

        [JsonPropertyName("detail")]
        public string? Detail { get; init; }

        [JsonPropertyName("retryAfterSeconds")]
        public int? RetryAfterSeconds { get; init; }
    }
}

/// <summary>Seeds accounts that can actually sign in, with a real hash of a known password.</summary>
public static class AuthenticationTestData
{
    /// <summary>The password every seeded account uses. Synthetic, and long enough for the policy.</summary>
    public const string Password = "correct-horse-battery-staple-77";

    /// <summary>
    /// Creates an active account whose password really is <see cref="Password"/>, under a sign-in name
    /// unique to this run.
    /// </summary>
    /// <remarks>
    /// The name is suffixed rather than fixed because the test database is migrated and not dropped
    /// between runs, and the sign-in name is unique per organisation. A fixed name passes once and then
    /// fails for the rest of the machine's life, which is the least useful way for a suite to break.
    /// </remarks>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="prefix">A short, readable prefix so a row can be traced back to its test.</param>
    public static async Task<StaffUser> CreateSignInReadyUserAsync(
        WebApplicationFixture fixture,
        string prefix)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var userName = $"{prefix}-{Guid.CreateVersion7():n}"[..Math.Min(prefix.Length + 13, 40)];

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hashing = scope.ServiceProvider.GetRequiredService<IPasswordHashingService>();
        var clock = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Time.IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Identifiers.IIdGenerator>();

        var now = clock.UtcNow;

        var user = StaffUser.Invite(
            ids.NewId(),
            SessionTestData.OrganisationId,
            userName,
            $"{userName}@synthetic.invalid",
            $"Test {userName}",
            now,
            SessionTestData.HomeBranchId).Value;

        user.SetPassword(ids.NewId(), hashing.Hash(user, Password), hashing.AlgorithmName, now, by: null)
            .IsSuccess.ShouldBeTrue();

        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return user;
    }

    /// <summary>Re-reads an account, so a test can assert on what a request changed.</summary>
    public static async Task<StaffUser?> ReloadAsync(WebApplicationFixture fixture, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == userId, TestContext.Current.CancellationToken);
    }
}
