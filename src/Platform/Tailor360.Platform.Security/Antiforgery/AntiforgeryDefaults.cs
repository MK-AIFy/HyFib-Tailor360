namespace Tailor360.Platform.Security.Antiforgery;

/// <summary>Names shared by the anti-forgery cookie, the request header and the client that reads them.</summary>
public static class AntiforgeryDefaults
{
    /// <summary>
    /// The cookie half of the token pair. It carries the <c>__Host-</c> prefix for the same reason the
    /// session cookie does, and it stays <c>HttpOnly</c>: the half that script needs is the request
    /// token, which arrives in a response body, so nothing is gained by letting script read this one.
    /// </summary>
    public const string CookieName = "__Host-t360.csrf";

    /// <summary>
    /// The header the client sends the request token in. A custom header is itself a small barrier —
    /// a cross-site form cannot set one — but the value is what is actually checked.
    /// </summary>
    public const string HeaderName = "X-CSRF-Token";
}
