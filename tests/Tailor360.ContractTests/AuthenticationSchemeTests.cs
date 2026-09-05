using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
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
}
