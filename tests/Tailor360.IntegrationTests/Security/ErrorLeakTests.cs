using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Shouldly;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.IntegrationTests.Security;

/// <summary>
/// What a failure tells the caller. Nothing about how this application is built.
/// </summary>
/// <remarks>
/// <para>
/// A stack trace names types, namespaces, package versions and the absolute path the build ran from.
/// An exception message frequently names a table, a column, a connection string or the value that was
/// rejected. Both are a map for the next attempt, and the second is a disclosure of whatever was in the
/// request. So the rule is the same for every status: the body carries the problem type, a sentence
/// written for a person, and the correlation identifier that lets somebody with access find the log
/// entry where the details actually are.
/// </para>
/// <para>
/// The two failures worth separating are a handler that throws and a <em>filter</em> that throws. A
/// filter runs before the handler is invoked and after the response has begun to take shape, and the
/// exception-handling middleware treats the two differently often enough that testing only the first
/// would leave the second undiscovered until it happened in production. Both are asserted here against
/// the composed application, so what is being tested is this pipeline and not a reconstruction of it.
/// </para>
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ErrorLeakTests(WebApplicationFixture fixture)
{
    /// <summary>
    /// Substrings that must never appear in a response body. Each is a marker of one of the three
    /// disclosures: the stack, the exception, and the machine the build ran on.
    /// </summary>
    private static readonly string[] Leaks =
    [
        FaultEndpoints.Sentinel,
        FaultEndpoints.SecretPath,
        "   at ",
        ".cs:line",
        "StackTrace",
        "stackTrace",
        "System.InvalidOperationException",
        "Npgsql",
        "Microsoft.AspNetCore",
        "Tailor360.",
        "/home/",
        "C:\\",
    ];

    /// <summary>An exception in the handler is answered with a problem document and nothing else.</summary>
    [Fact]
    public async Task AnExceptionInAHandlerDisclosesNothing()
        => await AssertNoLeakAsync(FaultEndpoints.HandlerPath, HttpStatusCode.InternalServerError);

    /// <summary>
    /// An exception in an endpoint filter is answered the same way. It is a different code path: the
    /// filter pipeline runs inside the endpoint middleware, so an exception there unwinds through a
    /// different set of frames than one thrown by the handler it was wrapping.
    /// </summary>
    [Fact]
    public async Task AnExceptionInAnEndpointFilterDisclosesNothing()
        => await AssertNoLeakAsync(FaultEndpoints.FilterPath, HttpStatusCode.InternalServerError);

    /// <summary>
    /// The failures a client provokes without any help: a route that does not exist, a method the route
    /// does not accept, a body the endpoint cannot read, and a body that is not JSON at all.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/v1/does-not-exist", null, null)]
    [InlineData("GET", "/api/version/extra", null, null)]
    [InlineData("PUT", "/api/version", null, null)]
    [InlineData("POST", "/api/v1/auth/login", "{ not json", "application/json")]
    [InlineData("POST", "/api/v1/auth/login", "<xml/>", "application/xml")]
    [InlineData("POST", "/api/v1/auth/login", "{\"userName\": 42}", "application/json")]
    public async Task AMalformedRequestDisclosesNothing(
        string method,
        string path,
        string? body,
        string? contentType)
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "192.0.2.91");

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Origin", "https://localhost");

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType!);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeFalse();
        await AssertBodyIsCleanAsync(response);
    }

    /// <summary>
    /// A failing response never carries the server's identity either. It is not a secret, but it is a
    /// free hint about which advisories are worth trying.
    /// </summary>
    [Fact]
    public async Task AFailingResponseNamesNoServerSoftware()
    {
        using var client = FaultClient();

        var response = await client.GetAsync(
            new Uri(FaultEndpoints.HandlerPath, UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.Contains("X-Powered-By").ShouldBeFalse();
        response.Headers.Contains("X-AspNet-Version").ShouldBeFalse();
    }

    /// <summary>
    /// The correlation identifier survives the failure. It is the whole reason a caller can be told
    /// nothing: somebody with access can find the log entry, and the caller can quote the reference.
    /// </summary>
    [Fact]
    public async Task AFailureStillCarriesTheCorrelationIdentifier()
    {
        using var client = FaultClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, FaultEndpoints.HandlerPath);
        request.Headers.Add("X-Correlation-Id", "errorleak42");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Correlation-Id").ShouldContain("errorleak42");
    }

    /// <summary>
    /// The security headers are applied to a failure too. A policy that lapses on the error path is a
    /// policy an attacker only has to provoke an error to remove.
    /// </summary>
    [Fact]
    public async Task AFailureStillCarriesTheSecurityHeaders()
    {
        using var client = FaultClient();

        var response = await client.GetAsync(
            new Uri(FaultEndpoints.HandlerPath, UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'self'");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
    }

    private async Task AssertNoLeakAsync(string path, HttpStatusCode expected)
    {
        using var client = FaultClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(expected);
        await AssertBodyIsCleanAsync(response);
    }

    private static async Task AssertBodyIsCleanAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var found = Leaks
            .Where(leak => body.Contains(leak, StringComparison.OrdinalIgnoreCase))
            .ToList();

        found.ShouldBeEmpty(
            $"The {(int)response.StatusCode} response discloses {string.Join(", ", found)}:\n{body}");
    }

    /// <summary>A client whose application has two routes that fail on purpose.</summary>
    private HttpClient FaultClient()
    {
        var factory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(provider => new FaultRouteFilter(provider))));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "192.0.2.92");
        return client;
    }
}

/// <summary>
/// Two routes that throw, added to the composed application by the test rather than by the product.
/// </summary>
/// <remarks>
/// <para>
/// It is added to the composed application through <see cref="FaultRouteFilter"/>, which is what lets a
/// test put a failing route into the <em>real</em> pipeline — behind the real exception handler, the
/// real problem-details service and the real security headers — without the product ever shipping a
/// route whose purpose is to fail.
/// </para>
/// <para>
/// The routes are built through the ordinary route builder rather than assembled by hand, so the one
/// that fails in a filter really does fail in a filter: the filter pipeline is compiled into the
/// endpoint's delegate at map time, and reproducing that by hand would be reproducing the thing under
/// test.
/// </para>
/// <para>
/// The exception carries a sentinel and a fabricated absolute path, so that a test can assert their
/// absence rather than assert the absence of "a stack trace" in general. If either string reaches the
/// caller, so would everything else in that exception.
/// </para>
/// </remarks>
internal sealed class FaultEndpoints : EndpointDataSource
{
    /// <summary>The route whose handler throws.</summary>
    public const string HandlerPath = "/api/v1/fault-probe/handler";

    /// <summary>The route whose endpoint filter throws before the handler runs.</summary>
    public const string FilterPath = "/api/v1/fault-probe/filter";

    /// <summary>A string that exists nowhere else, so finding it in a body proves a leak.</summary>
    public const string Sentinel = "fault-probe-sentinel-8f21";

    /// <summary>A fabricated build path, standing in for the one a real stack trace carries.</summary>
    public const string SecretPath = "/home/build-agent/tailor360/src/secret.cs";

    private const string Justification =
        "A route the integration tests add to the composed application so that a failure can be "
        + "provoked in the real pipeline. The product never maps it.";

    private readonly List<EndpointDataSource> _sources;

    public FaultEndpoints(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var routes = new TestRoutes(services);

        routes.MapGet(HandlerPath, (HttpContext _) => Throw("the handler"))
            .AllowAnonymousWithJustification(Justification, "#53")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultIp);

        routes.MapGet(FilterPath, () => Results.Ok("unreachable"))
            .AddEndpointFilter((_, _) => Throw("an endpoint filter"))
            .AllowAnonymousWithJustification(Justification, "#53")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultIp);

        _sources = [.. routes.DataSources];
    }

    public override IReadOnlyList<Endpoint> Endpoints => [.. _sources.SelectMany(source => source.Endpoints)];

    public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);

    private static ValueTask<object?> Throw(string where)
        => throw new InvalidOperationException(
            $"{Sentinel}: {where} failed while reading {SecretPath}.");

    /// <summary>
    /// The smallest thing that is an <see cref="IEndpointRouteBuilder"/>: somewhere to put data
    /// sources and a service provider to resolve filters against.
    /// </summary>
    private sealed class TestRoutes(IServiceProvider services) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = services;

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

/// <summary>
/// Adds <see cref="FaultEndpoints"/> to the route table of the composed application.
/// </summary>
/// <remarks>
/// The obvious registration — <c>services.AddSingleton&lt;EndpointDataSource, FaultEndpoints&gt;()</c> —
/// compiles, resolves, and does nothing. Routing composes the route table from the sources held by the
/// <see cref="IEndpointRouteBuilder"/> the application built itself, not from every
/// <see cref="EndpointDataSource"/> in the container, so a source registered only in the container is
/// present in dependency injection and absent from the router: the request falls through to the
/// <c>/api/{**path}</c> catch-all and answers 404. That failure looks exactly like a test asserting the
/// wrong status, which is why it is written down here rather than rediscovered.
///
/// Running <c>UseEndpoints</c> after the application has configured itself adds the source to the
/// builder the router does read, which is what puts these two routes into the real pipeline — the whole
/// point of the exercise, since a reconstruction of the pipeline would not prove anything about it.
/// </remarks>
internal sealed class FaultRouteFilter(IServiceProvider services) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            next(app);
            app.UseEndpoints(routes => routes.DataSources.Add(new FaultEndpoints(services)));
        };
}
