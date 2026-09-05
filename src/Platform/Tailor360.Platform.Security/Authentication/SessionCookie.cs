using Microsoft.AspNetCore.Http;

namespace Tailor360.Platform.Security.Authentication;

/// <summary>
/// Reads, writes and clears the session cookie. Every attribute on it is fixed here rather than
/// configurable, because each one is a security property and a deployment that could weaken one would
/// eventually weaken one.
/// </summary>
/// <remarks>
/// The attributes and why they are what they are:
/// <list type="bullet">
/// <item><description>
/// <c>__Host-</c> prefix — a browser accepts the cookie only when it is <c>Secure</c>, has
/// <c>Path=/</c> and carries no <c>Domain</c>. That makes it impossible for a sibling host, or for
/// anything reached over plain HTTP, to set or overwrite it. Cookie-tossing from a subdomain, which is
/// how a session-fixation attack usually plants a ticket, stops being available at all.
/// </description></item>
/// <item><description>
/// <c>HttpOnly</c> — script cannot read it. Combined with the anti-forgery token being the one value
/// script <em>can</em> read, cross-site scripting cannot lift a session out of the browser.
/// </description></item>
/// <item><description>
/// <c>SameSite=Lax</c> — the cookie does not ride along on a cross-site form post. It is a defence in
/// depth rather than the defence: anti-forgery validation and the origin check do not rely on it.
/// </description></item>
/// <item><description>
/// No <c>Expires</c> and no <c>Max-Age</c> — a browser-session cookie. Closing the browser drops the
/// ticket, which is what a shared counter device needs; the durable answer to "when does this end" is
/// the server-side row, never the cookie.
/// </description></item>
/// </list>
/// </remarks>
public static class SessionCookie
{
    /// <summary>Returns the presented cookie value, or null when none was sent.</summary>
    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var value = request.Cookies[SessionAuthenticationDefaults.CookieName];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Places a session cookie on the response.</summary>
    /// <param name="response">The response.</param>
    /// <param name="token">The opaque session value. Never logged, never returned in a body.</param>
    public static void Issue(HttpResponse response, string token)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        response.Cookies.Append(SessionAuthenticationDefaults.CookieName, token, Attributes());
    }

    /// <summary>
    /// Removes the session cookie. The attributes have to match the ones it was set with or the browser
    /// keeps the original and the person appears to be unable to sign out.
    /// </summary>
    public static void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(SessionAuthenticationDefaults.CookieName, Attributes());
    }

    private static CookieOptions Attributes() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
    };
}
