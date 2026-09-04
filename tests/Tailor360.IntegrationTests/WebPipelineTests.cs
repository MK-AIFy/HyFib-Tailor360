using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Tailor360.IntegrationTests;

/// <summary>The request pipeline as a client actually experiences it.</summary>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class WebPipelineTests(WebApplicationFixture fixture)
{
    [Fact]
    public async Task VersionEndpointDescribesTheBuildAndEnvironment()
    {
        using var client = fixture.CreateClient();

        var version = await client.GetFromJsonAsync<VersionPayload>(
            new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        version.ShouldNotBeNull();
        version.Version.ShouldNotBeNullOrWhiteSpace();
        version.BuildHash.ShouldNotBeNullOrWhiteSpace();

        // Anything other than "production" makes the client shell show the training banner, so the
        // value has to be exact and lower case.
        version.Environment.ShouldBe("development");
    }

    [Fact]
    public async Task EveryResponseCarriesTheSecurityHeaders()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.GetValues("Referrer-Policy").ShouldContain("same-origin");

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("default-src 'self'");
        csp.ShouldContain("object-src 'none'");
        csp.ShouldContain("frame-ancestors 'none'");
        csp.ShouldContain("nonce-");
        csp.ShouldNotContain("unsafe-eval");
    }

    [Fact]
    public async Task EachResponseCarriesAFreshContentSecurityPolicyNonce()
    {
        using var client = fixture.CreateClient();

        var first = await client.GetAsync(new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);
        var second = await client.GetAsync(new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        // A reused nonce is no better than no nonce: an injected script could carry the known value.
        first.Headers.GetValues("Content-Security-Policy").Single()
            .ShouldNotBe(second.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task TheCorrelationIdentifierIsEchoedBack()
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/version", UriKind.Relative));
        request.Headers.Add("X-Correlation-Id", "integration-test-42");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Correlation-Id").ShouldContain("integration-test-42");
    }

    [Fact]
    public async Task AHostileCorrelationIdentifierIsReplaced()
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/version", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "forged\" level=\"Fatal");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var echoed = response.Headers.GetValues("X-Correlation-Id").Single();
        echoed.ShouldNotContain("forged");
        echoed.ShouldAllBe(c => char.IsAsciiLetterOrDigit(c));
    }

    [Fact]
    public async Task AnUnknownApiRouteIsNotSwallowedByTheClientShellFallback()
    {
        using var client = fixture.CreateClient();

        // The fallback exists for client routes. An unknown /api path must still be a 404, or a typo in
        // a client call would silently receive HTML and fail much later.
        var response = await client.GetAsync(
            new Uri("/api/v1/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed record VersionPayload(string Version, string BuildHash, string Environment);
}
