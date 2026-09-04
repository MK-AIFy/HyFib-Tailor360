using Microsoft.AspNetCore.Http;

namespace Tailor360.Web.Middleware;

/// <summary>
/// Applies the response security headers, including a content security policy with a per-response
/// nonce. The policy is enforcing from the first release rather than report-only, because a policy that
/// is only tightened later tends never to be tightened at all; issue #53 narrows it further as the
/// client's needs become known.
/// </summary>
/// <param name="next">The next middleware.</param>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>The request item under which the per-response nonce is published to the renderer.</summary>
    public const string NonceItemKey = "csp-nonce";

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        context.Items[NonceItemKey] = nonce;

        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            $"script-src 'self' 'nonce-{nonce}'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "media-src 'self' blob:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "worker-src 'self' blob:; " +
            "manifest-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "upgrade-insecure-requests";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "same-origin";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        // The scan screen needs the camera; nothing else is granted.
        headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(), payment=()";

        await next(context);
    }
}
