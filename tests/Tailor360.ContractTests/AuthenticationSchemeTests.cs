using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Platform.Security.Antiforgery;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.ContractTests;

/// <summary>
/// ARCH-019 and the cookie configuration behind it, checked against the composed application rather
/// than against source text.
/// </summary>
/// <remarks>
/// One scheme is a structural property, not a coding style. An endpoint that accepts two schemes
/// accepts the weaker of them, and the weakness is invisible at the call site: the endpoint declares a
/// permission, the permission is checked, and the credential that satisfied it was a long-lived bearer
/// token nobody meant to accept there. Asserting the count keeps the rule true by construction, so a
/// second scheme added for an external API cannot leak into the browser surface unnoticed.
/// </remarks>
[Collection(WebHostCollection.Name)]
[Trait("Category", "Contract")]
public sealed class AuthenticationSchemeTests(WebHostFixture fixture)
{
    /// <summary>ARCH-019: the browser surface registers exactly one authentication scheme.</summary>
    [Fact]
    public async Task Arch019_TheApplicationRegistersExactlyOneAuthenticationScheme()
    {
        using var scope = fixture.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();

        var schemes = (await provider.GetAllSchemesAsync()).ToList();

        schemes.Count.ShouldBe(
            1,
            "ARCH-019: an endpoint that accepts more than one scheme accepts the weakest of them. "
            + "External API keys and OAuth clients belong on their own path and their own host "
            + "composition, not alongside the browser session. Registered: "
            + string.Join(", ", schemes.Select(scheme => scheme.Name)));

        schemes[0].Name.ShouldBe(SessionAuthenticationDefaults.Scheme);
        schemes[0].HandlerType.ShouldBe(typeof(SessionAuthenticationHandler));
    }

    /// <summary>
    /// ARCH-019, per endpoint: no route's authorisation metadata names more than one scheme, and no
    /// route on the browser surface names anything but the session cookie.
    /// </summary>
    /// <remarks>
    /// The assertion above is about what the application registers; this one is about what each route
    /// asks for. They are both needed and they fail on different days: registering a second scheme for
    /// <c>/api/ext/v1/**</c> is a legitimate change that relaxes the first, and on that day this is the
    /// assertion that stops the second scheme leaking on to a browser route.
    /// </remarks>
    [Fact]
    public void Arch019_NoEndpointNamesMoreThanOneAuthenticationScheme()
    {
        var routes = AuthenticationSchemeInspector.Read(EndpointSources());

        routes.ShouldNotBeEmpty();

        var complaints = AuthenticationSchemeInspector.Inspect(
            routes, SessionAuthenticationDefaults.Scheme, BrowserSurfacePrefix);

        complaints.ShouldBeEmpty(
            "ARCH-019: these endpoints do not accept exactly one authentication scheme:\n"
            + string.Join('\n', complaints.Select(complaint => "  " + complaint)));
    }

    /// <summary>
    /// The detector still detects. An assertion that passes because every route names no scheme at all
    /// would pass just as happily if the reading were broken, so the rule is fed a route that breaks it.
    /// </summary>
    [Fact]
    public void Arch019DetectorCatchesAnEndpointAcceptingTwoSchemes()
    {
        var complaints = AuthenticationSchemeInspector.Inspect(
            [new("POST /api/v1/orders", "/api/v1/orders", ["ApiKey", SessionAuthenticationDefaults.Scheme])],
            SessionAuthenticationDefaults.Scheme,
            BrowserSurfacePrefix);

        complaints.ShouldNotBeEmpty();
        complaints[0].ShouldContain("names 2 authentication schemes");
    }

    /// <summary>The converse: one scheme, but the wrong one, on the browser surface.</summary>
    [Fact]
    public void Arch019DetectorCatchesAForeignSchemeOnTheBrowserSurface()
    {
        var complaints = AuthenticationSchemeInspector.Inspect(
            [new("POST /api/v1/orders", "/api/v1/orders", ["ApiKey"])],
            SessionAuthenticationDefaults.Scheme,
            BrowserSurfacePrefix);

        complaints.ShouldNotBeEmpty();
        complaints[0].ShouldContain("accepts " + SessionAuthenticationDefaults.Scheme + " only");
    }

    /// <summary>And it accepts what the rule permits, so it is not simply refusing everything.</summary>
    [Fact]
    public void Arch019DetectorAcceptsOneSchemeOnItsOwnSurface()
        => AuthenticationSchemeInspector.Inspect(
            [
                new("POST /api/v1/orders", "/api/v1/orders", [SessionAuthenticationDefaults.Scheme]),
                new("GET /api/v1/orders", "/api/v1/orders", []),
                new("POST /api/ext/v1/orders", "/api/ext/v1/orders", ["ApiKey"]),
            ],
            SessionAuthenticationDefaults.Scheme,
            BrowserSurfacePrefix).ShouldBeEmpty();

    [Fact]
    public async Task TheOneSchemeIsAlsoTheDefaultForEveryPurpose()
    {
        using var scope = fixture.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();

        // A default that names a scheme other than the registered one would make every policy that
        // does not name a scheme explicitly fail closed in a way nobody could explain.
        (await provider.GetDefaultAuthenticateSchemeAsync())
            .ShouldNotBeNull().Name.ShouldBe(SessionAuthenticationDefaults.Scheme);
        (await provider.GetDefaultChallengeSchemeAsync())
            .ShouldNotBeNull().Name.ShouldBe(SessionAuthenticationDefaults.Scheme);
    }

    [Fact]
    public void TheAntiForgeryCookieIsConfiguredForTheHostPrefix()
    {
        using var scope = fixture.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        // The __Host- prefix is a promise to the browser: Secure, Path=/, no Domain. A browser rejects
        // the cookie outright if any of them is missing, so the application would issue tokens that
        // never come back and every state change would be refused.
        options.Cookie.Name.ShouldBe(AntiforgeryDefaults.CookieName);
        options.Cookie.Name.ShouldStartWith("__Host-");
        options.Cookie.SecurePolicy.ShouldBe(CookieSecurePolicy.Always);
        options.Cookie.Path.ShouldBe("/");
        options.Cookie.Domain.ShouldBeNull();
        options.Cookie.HttpOnly.ShouldBeTrue();
        options.Cookie.SameSite.ShouldBe(SameSiteMode.Lax);
        options.HeaderName.ShouldBe(AntiforgeryDefaults.HeaderName);
    }

    [Fact]
    public void TheSessionLifetimesAreTheOnesTheIssueSets()
    {
        using var scope = fixture.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<SessionAuthenticationOptions>>().Value;

        options.IdleTimeout.ShouldBe(TimeSpan.FromMinutes(30));
        options.AbsoluteLifetime.ShouldBe(TimeSpan.FromHours(12));
        options.IsUsable.ShouldBeTrue();
    }

    /// <summary>The prefix of the surface the session cookie is the only credential for.</summary>
    private const string BrowserSurfacePrefix = "/api/v1/";

    private IEnumerable<EndpointDataSource> EndpointSources()
    {
        using var scope = fixture.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>()];
    }
}
