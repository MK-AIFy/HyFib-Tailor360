namespace Tailor360.Platform.Security.Authentication;

/// <summary>Names for the session authentication scheme used by the backend-for-frontend.</summary>
public static class SessionAuthenticationDefaults
{
    /// <summary>
    /// The single authentication scheme. Sessions are server-side and carried by a <c>__Host-</c>
    /// prefixed cookie (ADR-0006); there is deliberately no bearer-token scheme for browsers.
    /// </summary>
    public const string Scheme = "Tailor360.Session";

    /// <summary>The session cookie name. The <c>__Host-</c> prefix binds it to the exact origin and to HTTPS.</summary>
    public const string CookieName = "__Host-t360.session";
}
