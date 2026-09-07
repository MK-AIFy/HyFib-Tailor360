using Microsoft.AspNetCore.Http;

namespace Tailor360.Modules.Identity.Api;

/// <summary>
/// Reads, writes and clears the remembered-device cookie.
/// </summary>
/// <remarks>
/// It carries the same attributes as the session cookie and for the same reasons — the <c>__Host-</c>
/// prefix so no sibling host and nothing over plain HTTP can set it, <c>HttpOnly</c> so script cannot
/// read it, <c>SameSite=Lax</c> so it does not ride along on a cross-site post — with one difference:
/// this one has an explicit expiry, because remembering a device across browser restarts is the entire
/// point of it. The server-side row is still what decides whether it works; the cookie expiry only
/// stops the browser sending a value that stopped working weeks ago.
/// </remarks>
public static class TrustedDeviceCookie
{
    /// <summary>The cookie name. <c>__Host-</c> for the same reasons as the session cookie.</summary>
    public const string Name = "__Host-t360.device";

    /// <summary>Returns the presented value, or null when none was sent.</summary>
    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var value = request.Cookies[Name];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Places a remembered-device cookie on the response.</summary>
    public static void Issue(HttpResponse response, string token, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var attributes = Attributes();
        attributes.Expires = expiresAt;

        response.Cookies.Append(Name, token, attributes);
    }

    /// <summary>Removes the cookie. The attributes must match the ones it was set with.</summary>
    public static void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(Name, Attributes());
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
