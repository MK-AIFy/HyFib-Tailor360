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
    public async Task VersionEndpointDescribesTheBuildTheSchemaAndTheSupportedClientRange()
    {
        using var client = fixture.CreateClient();

        var version = await client.GetFromJsonAsync<VersionPayload>(
            new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        version.ShouldNotBeNull();
        version.Api.ShouldBe("v1");
        version.Current.ShouldNotBeNullOrWhiteSpace();

        // Anything other than "production" makes the client shell show the training banner, so the
        // value has to be exact and lower case.
        version.Environment.ShouldBe("development");

        // The schema version is the newest migration timestamp this build carries, so it is fourteen
        // digits — never the sentinel, because this build carries migrations.
        version.SchemaVersion.ShouldNotBeNull();
        version.SchemaVersion.Length.ShouldBe(14);
        version.SchemaVersion.ShouldAllBe(character => char.IsAsciiDigit(character));

        // `minimumClient` is what a client compares itself against to decide whether to prompt for an
        // update. It is present and empty here, which is the honest answer before a release: nothing
        // has shipped that a client could be older than.
        version.MinimumClient.ShouldNotBeNull();
    }

    /// <summary>
    /// The revision is a Development-only member. In a deployed environment it would tell an
    /// unauthenticated caller exactly which published advisories apply to what is running.
    /// </summary>
    [Fact]
    public async Task TheVersionEndpointDisclosesTheRevisionOnlyInDevelopment()
    {
        using var client = fixture.CreateClient();

        var body = await client.GetStringAsync(
            new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        // The fixture hosts Development, so this asserts the member exists at all — the withholding is
        // asserted where the environment can be changed, in ClientVersionHandshakeTests.
        body.ShouldContain("\"commit\"");
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

    [Theory]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData("/api/nope")]
    [InlineData("/health/nope")]
    public async Task AnUnknownApiOrHealthRouteIsNotSwallowedByTheClientShellFallback(string path)
    {
        using var client = fixture.CreateClient();

        // The shell fallback exists for client routes. Once the built client is present in wwwroot it
        // would otherwise answer a mistyped API call with HTML and a 200, and the caller would fail
        // somewhere far from the cause. Asserting the content type as well as the status keeps this
        // test from passing merely because no index.html happens to be deployed.
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/html");
    }

    [Fact]
    public async Task AClientDeepLinkResolvesToTheShell()
    {
        using var client = fixture.CreateClient();

        // A barcode label or a message can carry a deep link straight to a job. It has to open.
        var response = await client.GetAsync(
            new Uri("/orders/12345", UriKind.Relative), TestContext.Current.CancellationToken);

        // Without a built client in wwwroot there is nothing to fall back to, which is the state of a
        // backend-only checkout; either answer is acceptable, serving the API instead is not.
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        }
    }

    private sealed record VersionPayload(
        string Api,
        string MinimumClient,
        string Current,
        string Environment,
        string SchemaVersion,
        string? Commit);
}
