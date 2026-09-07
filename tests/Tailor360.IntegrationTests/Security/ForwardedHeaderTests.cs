using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Tailor360.Web.Configuration;

namespace Tailor360.IntegrationTests.Security;

/// <summary>
/// Who is allowed to tell this application where a request came from.
/// </summary>
/// <remarks>
/// <para>
/// <c>X-Forwarded-For</c> is a request header, which means it is whatever the caller typed. Everything
/// downstream of stage 1 of the pipeline reads the client address it produces: the address-partitioned
/// rate limits, the abuse throttles, the audit trail. So a caller that can set its own address gets a
/// private rate-limit partition per request, and puts somebody else's address in the record of what it
/// did.
/// </para>
/// <para>
/// The tests come in two halves. The first exercises
/// <see cref="ForwardedHeadersOptionsSetup"/> and the framework middleware directly, because "trusted"
/// and "not trusted" are properties of the configuration and are cheapest to assert where the
/// configuration is. The second is the consequence, asserted end to end against the composed
/// application: a caller varying <c>X-Forwarded-For</c> on every request does not thereby escape a
/// per-address limit.
/// </para>
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ForwardedHeaderTests(WebApplicationFixture fixture)
{
    private const string ProxyNetwork = "10.20.0.0/16";
    private const string ProxyAddress = "10.20.0.7";
    private const string SomewhereElse = "198.51.100.4";
    private const string ClaimedAddress = "203.0.113.9";

    /// <summary>
    /// The deployed default: no proxy is trusted until one is named, so the header does nothing.
    /// </summary>
    [Fact]
    public void WithNoTrustedProxyTheHeadersAreNotProcessedAtAll()
    {
        var options = Configure(Environments.Development);

        options.ForwardedHeaders.ShouldBe(ForwardedHeaders.None);
        options.KnownProxies.ShouldBeEmpty();
        options.KnownIPNetworks.ShouldBeEmpty();
    }

    /// <summary>
    /// The framework's defaults trust loopback. That is right for a sidecar proxy and wrong for
    /// anything else, so they are cleared and re-stated rather than inherited — and a host that
    /// inherited them would trust a forwarded header from anything sharing its network namespace.
    /// </summary>
    [Fact]
    public void TheFrameworkDefaultsAreNotInherited()
    {
        var options = new ForwardedHeadersOptions();
        options.KnownProxies.ShouldNotBeEmpty("The framework's own default is what is being replaced.");

        Configure(Environments.Development, options);

        options.KnownProxies.ShouldBeEmpty();
        options.KnownIPNetworks.ShouldBeEmpty();
    }

    /// <summary>Only one hop is unwound, because the deployment puts exactly one proxy in front.</summary>
    [Fact]
    public void OnlyOneHopIsUnwound()
        => Configure(Environments.Production, settings: (ProxyNetworkKey, ProxyNetwork))
            .ForwardLimit.ShouldBe(1);

    /// <summary>
    /// Outside Development an unconfigured trusted set is a start-up failure rather than a silent
    /// "trust nothing". Trusting nothing sounds safe and is not: the client address becomes the proxy's
    /// own, so every caller shares one rate-limit partition and the audit trail names the load balancer
    /// for everything anybody does.
    /// </summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void OutsideDevelopmentAnUnconfiguredTrustedSetFailsValidation(string environment)
    {
        var setup = SetupFor(environment);
        var options = new ForwardedHeadersOptions();
        setup.Configure(options);

        var result = setup.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldBe(ForwardedHeadersOptionsSetup.NoTrustedProxyMessage);
    }

    /// <summary>A named proxy network satisfies validation; Development never needs one.</summary>
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public void ANamedProxyNetworkSatisfiesValidation(string environment, bool configured)
    {
        var settings = configured ? new[] { (ProxyNetworkKey, ProxyNetwork) } : [];
        var setup = SetupFor(environment, settings);
        var options = new ForwardedHeadersOptions();
        setup.Configure(options);

        setup.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>The reverse proxy is believed, which is the whole point of naming it.</summary>
    [Fact]
    public async Task AForwardedAddressFromTheTrustedProxyIsHonoured()
    {
        var resolved = await ResolveAsync(
            Configure(Environments.Production, settings: (ProxyNetworkKey, ProxyNetwork)),
            connectedFrom: ProxyAddress,
            forwardedFor: ClaimedAddress);

        resolved.ShouldBe(ClaimedAddress);
    }

    /// <summary>
    /// The spoof. A caller that is not the proxy sends the same header and is not believed: the address
    /// stays the one the socket reports, so it cannot choose its own rate-limit partition, cannot
    /// impersonate another shop's address in the audit trail, and cannot pass a source allowlist by
    /// claiming to be on it.
    /// </summary>
    [Fact]
    public async Task AForwardedAddressFromAnUntrustedSourceIsIgnored()
    {
        var resolved = await ResolveAsync(
            Configure(Environments.Production, settings: (ProxyNetworkKey, ProxyNetwork)),
            connectedFrom: SomewhereElse,
            forwardedFor: ClaimedAddress);

        resolved.ShouldBe(SomewhereElse);
    }

    /// <summary>
    /// A single named proxy address is trusted; its neighbour on the same network is not. Naming
    /// addresses rather than a network is the tighter of the two configurations, and it has to stay
    /// tighter.
    /// </summary>
    [Fact]
    public async Task AKnownProxyAddressIsTrustedAndItsNeighbourIsNot()
    {
        var options = Configure(Environments.Production, settings: (ProxyAddressKey, ProxyAddress));

        (await ResolveAsync(options, connectedFrom: ProxyAddress, forwardedFor: ClaimedAddress))
            .ShouldBe(ClaimedAddress);
        (await ResolveAsync(options, connectedFrom: "10.20.0.8", forwardedFor: ClaimedAddress))
            .ShouldBe("10.20.0.8");
    }

    /// <summary>
    /// A caller that prepends its own hop does not get to choose which one is believed. With a forward
    /// limit of one, only the address the trusted proxy appended is taken; the value the caller sent
    /// stays where it was.
    /// </summary>
    [Fact]
    public async Task AChainOfForwardedAddressesIsNotUnwoundPastTheTrustedHop()
    {
        var resolved = await ResolveAsync(
            Configure(Environments.Production, settings: (ProxyNetworkKey, ProxyNetwork)),
            connectedFrom: ProxyAddress,
            forwardedFor: $"{ClaimedAddress}, {SomewhereElse}");

        // The proxy appends the address it saw, so the last entry is the only trustworthy one.
        resolved.ShouldBe(SomewhereElse);
    }

    /// <summary>
    /// The consequence, end to end. A caller sending a different <c>X-Forwarded-For</c> on every
    /// request still spends one partition's permits, because the composed application does not trust
    /// the header from a caller that is not the proxy. If it did, this loop would never be refused.
    /// </summary>
    [Fact]
    public async Task ASpoofedForwardedAddressDoesNotEscapeThePerAddressLimit()
    {
        // Its own client address, so this loop spends its own partition rather than a partition another
        // test in the collection is also using.
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "192.0.2.77");

        var permits = RateLimitPolicies.Catalogue
            .Single(policy => policy.Name == "default-ip")
            .Permits;

        for (var attempt = 1; attempt <= permits; attempt++)
        {
            var allowed = await SendAsync(client, attempt);
            allowed.StatusCode.ShouldBe(
                HttpStatusCode.NotFound,
                $"Attempt {attempt} of {permits} is within the limit and should have been served.");
        }

        var refused = await SendAsync(client, permits + 1);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.ShouldNotBeNull();
        refused.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await refused.Content.ReadFromJsonAsync<RefusalPayload>(
            TestContext.Current.CancellationToken);

        problem.ShouldNotBeNull();
        problem.Code.ShouldBe("rate.limited");
        problem.Retryable.ShouldBeTrue();
        problem.RetryAfterSeconds.ShouldBeGreaterThan(0);
    }

    private const string ProxyNetworkKey = "ForwardedHeaders:KnownNetworks:0";
    private const string ProxyAddressKey = "ForwardedHeaders:KnownProxies:0";

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, int attempt)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/forwarded-header-probe/{attempt}");

        // A different claimed address every time. Believed, this would be a fresh partition per
        // request and the limit would never be reached.
        request.Headers.Add("X-Forwarded-For", $"203.0.113.{attempt % 250}");

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static ForwardedHeadersOptionsSetup SetupFor(
        string environment,
        params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(
                setting => setting.Key,
                setting => (string?)setting.Value))
            .Build();

        return new ForwardedHeadersOptionsSetup(configuration, new StubEnvironment(environment));
    }

    private static ForwardedHeadersOptions Configure(
        string environment,
        ForwardedHeadersOptions? options = null,
        params (string Key, string Value)[] settings)
    {
        options ??= new ForwardedHeadersOptions();
        SetupFor(environment, settings).Configure(options);
        return options;
    }

    /// <summary>
    /// Runs one request through the framework's forwarded-headers middleware configured exactly as the
    /// host configures it, and reports the client address everything downstream would read.
    /// </summary>
    private static async Task<string> ResolveAsync(
        ForwardedHeadersOptions options,
        string connectedFrom,
        string forwardedFor)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    // The socket's own view of the caller. In a deployment this is set by Kestrel from
                    // the connection; here the test says what the connection was.
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(connectedFrom);
                        await next(context);
                    });

                    app.UseForwardedHeaders(options);
                    app.Run(context => context.Response.WriteAsync(
                        context.Connection.RemoteIpAddress?.ToString() ?? "(none)"));
                }))
            .StartAsync(TestContext.Current.CancellationToken);

        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The refusal a client receives, as the client reads it.</summary>
    private sealed record RefusalPayload(string Code, bool Retryable, int RetryAfterSeconds);

    /// <summary>The one thing the setup asks the environment: whether this is Development.</summary>
    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tailor360.Web";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; }
            = new PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
