using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Platform.Security.Antiforgery;

namespace Tailor360.UnitTests.Platform.Sessions;

/// <summary>
/// The origin and fetch-metadata check. Each case is a request a browser can actually make, and the
/// ones that must be refused are asserted as refusals rather than as "not obviously allowed".
/// </summary>
[Trait("Category", "Unit")]
public sealed class OriginValidationTests
{
    private const string Host = "tailor360.example.com";

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task ASafeMethodIsNeverRefused(string method)
    {
        var context = Request(method, fetchSite: "cross-site", origin: "https://evil.example.com");

        var reached = await InvokeAsync(context);

        reached.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task ACrossSiteStateChangeIsRefused()
    {
        var context = Request("POST", fetchSite: "cross-site");

        var reached = await InvokeAsync(context);

        reached.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task ASameSiteStateChangeIsRefused()
    {
        // SameSite=Lax considers any host under the same registrable domain to be same-site, so a
        // compromised sibling host would otherwise be able to make authenticated calls. The __Host-
        // prefix stops it setting the cookie; this stops it using one.
        var context = Request("POST", fetchSite: "same-site");

        var reached = await InvokeAsync(context);

        reached.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task AForeignOriginIsRefusedEvenWithoutFetchMetadata()
    {
        var context = Request("POST", origin: "https://evil.example.com");

        var reached = await InvokeAsync(context);

        reached.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task TheLiteralNullOriginIsRefused()
    {
        // A sandboxed frame, a data: document and a redirected cross-origin request all send "null".
        // None of them is the client.
        var context = Request("POST", origin: "null");

        var reached = await InvokeAsync(context);

        reached.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task TheApplicationsOwnOriginIsAllowed()
    {
        var context = Request("POST", fetchSite: "same-origin", origin: $"https://{Host}");

        var reached = await InvokeAsync(context);

        reached.ShouldBeTrue();
    }

    [Fact]
    public async Task AConfiguredAdditionalOriginIsAllowed()
    {
        var options = new RequestOriginOptions();
        options.AdditionalAllowedOrigins.Add("https://station.example.com");

        var context = Request("POST", origin: "https://station.example.com");

        var reached = await InvokeAsync(context, options);

        reached.ShouldBeTrue();
    }

    [Fact]
    public async Task ARequestDeclaringNeitherPassesByDefault()
    {
        // The command-line tool and the probes send neither header. Refusing them here would break
        // them for no gain: anti-forgery validation stands immediately behind this check.
        var context = Request("POST");

        var reached = await InvokeAsync(context);

        reached.ShouldBeTrue();
    }

    [Fact]
    public async Task ARequestDeclaringNeitherIsRefusedWhenTheDeploymentRequiresOne()
    {
        var context = Request("POST");

        var reached = await InvokeAsync(context, new RequestOriginOptions { RequireDeclaredOrigin = true });

        reached.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    private static async Task<bool> InvokeAsync(HttpContext context, RequestOriginOptions? options = null)
    {
        var reached = false;

        var middleware = new OriginValidationMiddleware(
            _ =>
            {
                reached = true;
                return Task.CompletedTask;
            },
            Options.Create(options ?? new RequestOriginOptions()),
            NullLogger<OriginValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        return reached;
    }

    private static DefaultHttpContext Request(string method, string? fetchSite = null, string? origin = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(Host);
        context.Request.Path = "/api/v1/orders";
        context.Response.Body = new MemoryStream();

        if (fetchSite is not null)
        {
            context.Request.Headers["Sec-Fetch-Site"] = fetchSite;
        }

        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        return context;
    }
}
