using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Tailor360.Platform.Security.Antiforgery;

namespace Tailor360.IntegrationTests.Security;

/// <summary>
/// The cross-site defences as a browser meets them: the anti-forgery token endpoint, the token
/// requirement on every state-changing request, and the origin check in front of both.
/// </summary>
/// <remarks>
/// The requests are made to a path that resolves to the API's 404 fallback. That is deliberate: it
/// isolates the middleware from any endpoint's own behaviour, so a 403 here is the pipeline refusing
/// the request and a 404 is the pipeline letting it through. Cookies are managed by hand because the
/// cookies under test carry <c>Secure</c>, and a cookie container would silently drop them over the
/// test host's plain-HTTP loopback address.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CrossSiteDefenceTests(WebApplicationFixture fixture)
{
    private const string StateChangingPath = "/api/v1/anything";
    private const string Origin = "https://localhost";

    [Fact]
    public async Task TheTokenEndpointIssuesAPairAndForbidsCaching()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            new Uri(AntiForgeryPath, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A cached token is either stale, which presents as unexplained refusals, or shared — and a
        // proxy that served one person's token to another would hand out half of a valid pair.
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();

        var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(
            TestContext.Current.CancellationToken);

        payload.ShouldNotBeNull();
        payload.Token.ShouldNotBeNullOrWhiteSpace();
        payload.HeaderName.ShouldBe(AntiforgeryDefaults.HeaderName);
    }

    [Fact]
    public async Task TheTokenCookieCarriesTheHostPrefixAndIsNotReadableByScript()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            new Uri(AntiForgeryPath, UriKind.Relative), TestContext.Current.CancellationToken);

        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(AntiforgeryDefaults.CookieName, StringComparison.Ordinal));

        setCookie.ShouldContain("path=/", Case.Insensitive);
        setCookie.ShouldContain("secure", Case.Insensitive);
        setCookie.ShouldContain("samesite=lax", Case.Insensitive);

        // The half script needs is the request token, which arrives in the body. Letting script read
        // the cookie half as well would hand cross-site scripting the whole pair.
        setCookie.ShouldContain("httponly", Case.Insensitive);
        setCookie.ShouldNotContain("domain=", Case.Insensitive);
    }

    [Fact]
    public async Task AStateChangingRequestWithoutATokenIsRefused()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, StateChangingPath);
        request.Headers.Add("Origin", Origin);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe(AntiforgeryEnforcementMiddleware.ErrorCode);
    }

    [Fact]
    public async Task AStateChangingRequestWithBothHalvesOfTheTokenPasses()
    {
        using var client = CreateClient();
        var (token, cookie) = await ObtainTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, StateChangingPath);
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add(AntiforgeryDefaults.HeaderName, token);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Through both middlewares and on to the router, which has no such route. A 404 here is the
        // proof that the refusals in the other tests came from the defences and not from the route.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ATokenWithoutItsCookieHalfIsRefused()
    {
        using var client = CreateClient();
        var (token, _) = await ObtainTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, StateChangingPath);
        request.Headers.Add("Origin", Origin);
        request.Headers.Add(AntiforgeryDefaults.HeaderName, token);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Half a pair is not a pair. A site that could read the request token but not set the cookie
        // must gain nothing from it.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe(AntiforgeryEnforcementMiddleware.ErrorCode);
    }

    [Fact]
    public async Task ACrossSiteStateChangeIsRefusedBeforeTheTokenIsEvenConsidered()
    {
        using var client = CreateClient();
        var (token, cookie) = await ObtainTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, StateChangingPath);
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add(AntiforgeryDefaults.HeaderName, token);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Even holding a valid token — which a confused-deputy or a leaked value could give an
        // attacker — the browser's own report that the request is cross-site ends it.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe(OriginValidationMiddleware.ErrorCode);
    }

    [Fact]
    public async Task AStateChangeClaimingAnotherOriginIsRefused()
    {
        using var client = CreateClient();
        var (token, cookie) = await ObtainTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, StateChangingPath);
        request.Headers.Add("Origin", "https://evil.example.com");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add(AntiforgeryDefaults.HeaderName, token);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).ShouldBe(OriginValidationMiddleware.ErrorCode);
    }

    [Fact]
    public async Task AReadIsNotAffectedByEitherDefence()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // A safe method changes nothing, so refusing it would only break the client for no gain.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ARefusalIsAProblemDetailsBodyCarryingTheCorrelationIdentifier()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, StateChangingPath);
        request.Headers.Add("Origin", "https://evil.example.com");
        request.Headers.Add("X-Correlation-Id", "cross-site-refusal-1");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(
            TestContext.Current.CancellationToken);

        problem.ShouldNotBeNull();
        problem.Type.ShouldBe($"urn:tailor360:problem:{OriginValidationMiddleware.ErrorCode}");
        problem.Status.ShouldBe(403);
        problem.CorrelationId.ShouldBe("cross-site-refusal-1");

        // A refusal must not explain how to pass: no header names, no expected values, no stack trace.
        problem.Detail.ShouldNotBeNullOrWhiteSpace();
        problem.Detail!.ShouldNotContain("Origin");
        problem.Detail.ShouldNotContain("Sec-Fetch-Site");

        // The correlation header survives the response being cleared, so a support engineer can still
        // tie the refusal to a log line.
        response.Headers.GetValues("X-Correlation-Id").ShouldContain("cross-site-refusal-1");
    }

    private const string AntiForgeryPath = "/api/v1/antiforgery";

    /// <summary>
    /// A client that speaks HTTPS and manages its own cookies. Both matter: the anti-forgery cookie is
    /// configured <c>Secure</c> — which a real deployment satisfies through the reverse proxy's
    /// <c>X-Forwarded-Proto</c> — and a cookie container would drop a Secure cookie rather than send it
    /// back, hiding the very behaviour under test.
    /// </summary>
    private HttpClient CreateClient()
        => fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private static async Task<(string Token, string Cookie)> ObtainTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync(
            new Uri(AntiForgeryPath, UriKind.Relative), TestContext.Current.CancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(
            TestContext.Current.CancellationToken);

        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(AntiforgeryDefaults.CookieName, StringComparison.Ordinal));

        var cookie = setCookie.Split(';', 2)[0];

        return (payload!.Token, cookie);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(
            TestContext.Current.CancellationToken);

        return problem?.Code;
    }

    private sealed record TokenPayload(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("headerName")] string HeaderName);

    private sealed record ProblemPayload(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("status")] int? Status,
        [property: JsonPropertyName("detail")] string? Detail,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("correlationId")] string? CorrelationId);
}
