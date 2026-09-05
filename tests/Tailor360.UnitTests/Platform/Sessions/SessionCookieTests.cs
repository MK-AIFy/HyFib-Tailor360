using Microsoft.AspNetCore.Http;
using Shouldly;
using Tailor360.Platform.Security.Antiforgery;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.UnitTests.Platform.Sessions;

/// <summary>
/// The attributes on the session cookie. Every one of them is a control rather than a preference, so
/// each is asserted individually: a review can see a name changed, but nobody notices <c>Secure</c>
/// quietly becoming false.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SessionCookieTests
{
    [Fact]
    public void TheCookieNameCarriesTheHostPrefix()
    {
        // The prefix is what stops a sibling host, or anything reached over plain HTTP, from setting
        // this cookie at all — which is how a fixation attack usually plants a ticket.
        SessionAuthenticationDefaults.CookieName.ShouldStartWith("__Host-");
        AntiforgeryDefaults.CookieName.ShouldStartWith("__Host-");
    }

    [Fact]
    public void IssuingSetsEveryAttributeTheHostPrefixRequires()
    {
        var context = new DefaultHttpContext();

        SessionCookie.Issue(context.Response, "an-opaque-value");

        var header = SetCookieHeader(context);

        header.ShouldContain($"{SessionAuthenticationDefaults.CookieName}=an-opaque-value");
        header.ShouldContain("path=/", Case.Insensitive);
        header.ShouldContain("secure", Case.Insensitive);
        header.ShouldContain("httponly", Case.Insensitive);
        header.ShouldContain("samesite=lax", Case.Insensitive);
    }

    [Fact]
    public void IssuingSetsNoDomainAndNoExpiry()
    {
        var context = new DefaultHttpContext();

        SessionCookie.Issue(context.Response, "an-opaque-value");

        var header = SetCookieHeader(context);

        // A Domain attribute would widen the cookie to sibling hosts and would make the browser reject
        // the __Host- prefix outright.
        header.ShouldNotContain("domain=", Case.Insensitive);

        // No Expires and no Max-Age: a browser-session cookie, so closing the browser drops the ticket.
        // How long the session really lasts is a column on the server, not a promise made to the client.
        header.ShouldNotContain("expires=", Case.Insensitive);
        header.ShouldNotContain("max-age=", Case.Insensitive);
    }

    [Fact]
    public void ClearingUsesTheSameAttributesSoTheBrowserActuallyRemovesIt()
    {
        var context = new DefaultHttpContext();

        SessionCookie.Clear(context.Response);

        var header = SetCookieHeader(context);

        // A deletion whose attributes do not match the ones the cookie was set with leaves the original
        // in place, and the person appears unable to sign out.
        header.ShouldContain("path=/", Case.Insensitive);
        header.ShouldContain("secure", Case.Insensitive);
        header.ShouldContain("httponly", Case.Insensitive);
        header.ShouldContain("samesite=lax", Case.Insensitive);
        header.ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
    }

    [Fact]
    public void ReadingReturnsNullWhenNoCookieWasPresented()
    {
        var context = new DefaultHttpContext();

        SessionCookie.Read(context.Request).ShouldBeNull();
    }

    [Fact]
    public void ReadingReturnsThePresentedValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{SessionAuthenticationDefaults.CookieName}=presented";

        SessionCookie.Read(context.Request).ShouldBe("presented");
    }

    private static string SetCookieHeader(HttpContext context)
        => context.Response.Headers.SetCookie.ToString();
}
