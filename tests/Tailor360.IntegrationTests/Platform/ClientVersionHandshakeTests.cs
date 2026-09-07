using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Tailor360.Web.Configuration;
using Tailor360.Web.Middleware;

namespace Tailor360.IntegrationTests.Platform;

/// <summary>
/// The client-version handshake of <c>docs/architecture/conventions.md</c> section 5.4: a build older
/// than this server supports is told so once, in a form it can act on, rather than being allowed to
/// fail one field at a time.
/// </summary>
/// <remarks>
/// The negative cases carry the weight here. A handshake that refused everything would be caught by any
/// test; the ways it goes wrong in practice are refusing a caller that never claimed a version, refusing
/// the very endpoint that carries the answer, and — the one that would make it useless — quietly
/// refusing nothing at all because the configured minimum did not parse.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClientVersionHandshakeTests(WebApplicationFixture fixture)
{
    private const string Probe = "/api/v1/me";

    [Fact]
    public async Task AClientOlderThanTheMinimumIsRefusedWithSomethingItCanAct0n()
    {
        using var client = ClientFor("2.0.0");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Probe, UriKind.Relative));
        request.Headers.Add(ClientVersionMiddleware.HeaderName, "1.9.9");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UpgradeRequired);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        problem.GetProperty("code").GetString().ShouldBe(ClientVersionMiddleware.ErrorCode);

        // The two versions are what the update prompt is written from. Without them the client can say
        // "something is out of date" and nothing else.
        problem.GetProperty("minimumClient").GetString().ShouldBe("2.0.0");
        problem.GetProperty("current").GetString().ShouldNotBeNullOrWhiteSpace();

        // Reloading fixes it; repeating the request does not, and a client that retried would spin.
        problem.GetProperty("retryable").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// The refusal is decided before the session is looked up, so a stale client is told to update
    /// rather than told it is not signed in — which is the answer it would otherwise get here, and the
    /// one that would send somebody to re-enter a password that was never the problem.
    /// </summary>
    [Fact]
    public async Task TheRefusalComesBeforeAuthentication()
    {
        using var client = ClientFor("2.0.0");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Probe, UriKind.Relative));
        request.Headers.Add(ClientVersionMiddleware.HeaderName, "1.0.0");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UpgradeRequired);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("2.0.1")]
    [InlineData("3.0.0")]
    public async Task AClientAtOrAboveTheMinimumIsNotRefusedByTheHandshake(string declared)
    {
        using var client = ClientFor("2.0.0");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Probe, UriKind.Relative));
        request.Headers.Add(ClientVersionMiddleware.HeaderName, declared);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // 401 is the right answer for an anonymous caller of this endpoint. What matters is that the
        // handshake let it through to be answered on its merits.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("1")]
    public async Task ACallerThatClaimsNoUsableVersionPassesThrough(string? declared)
    {
        using var client = ClientFor("2.0.0");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Probe, UriKind.Relative));
        if (declared is not null)
        {
            request.Headers.Add(ClientVersionMiddleware.HeaderName, declared);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // The health probes, the reverse proxy and the command-line tool are not the progressive web
        // application and have no build to compare; a value nobody can interpret is not "too old"
        // either. Neither is a hole — anything wanting to dodge the check could simply omit the header.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The exemption the whole design rests on: a refused client reads <c>minimumClient</c> from here,
    /// so refusing this too would leave the refusal unexplainable.
    /// </summary>
    [Fact]
    public async Task TheVersionEndpointItselfIsNeverRefused()
    {
        using var client = ClientFor("9.9.9");

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri("/api/version", UriKind.Relative));
        request.Headers.Add(ClientVersionMiddleware.HeaderName, "0.0.1");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        payload.GetProperty("minimumClient").GetString().ShouldBe("9.9.9");
    }

    [Fact]
    public async Task WithNoMinimumConfiguredNothingIsRefused()
    {
        using var client = ClientFor(string.Empty);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Probe, UriKind.Relative));
        request.Headers.Add(ClientVersionMiddleware.HeaderName, "0.0.1");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>A client of an application whose configured minimum is <paramref name="minimum"/>.</summary>
    private HttpClient ClientFor(string minimum)
    {
        var factory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{ClientCompatibilityOptions.SectionName}:MinimumClientVersion"] = minimum,
                })));

        var client = factory.CreateClient();

        // Its own rate-limit partition, so a run of these does not spend the shared anonymous budget.
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "192.0.2.54");
        return client;
    }
}
