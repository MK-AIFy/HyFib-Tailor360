using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Tailor360.Web.Middleware;

/// <summary>
/// Applies the response security headers, including an <b>enforcing</b> content security policy with a
/// per-response nonce.
/// </summary>
/// <remarks>
/// <para>
/// The policy has been enforcing since it was first written rather than report-only, because a policy
/// that is only tightened later tends never to be tightened at all. What #53 changed is its content:
/// <c>script-src</c> no longer names <c>'self'</c> and <c>style-src</c> no longer allows
/// <c>'unsafe-inline'</c>, so the policy now contains no <c>unsafe-</c> source of any kind and no host
/// source that an uploaded or reflected same-origin response could satisfy.
/// </para>
/// <para>
/// <b>Why <c>'strict-dynamic'</c> rather than <c>'self'</c>.</b> <c>script-src 'self'</c> trusts every
/// same-origin URL, and this application serves same-origin URLs whose bytes came from a customer — a
/// streamed media object, a stored design note echoed in an error. A script gadget that persuades the
/// browser to execute one of those defeats the policy without ever placing a <c>&lt;script&gt;</c> tag
/// in the shell. Under <c>'nonce-…' 'strict-dynamic'</c> the browser executes the one script the server
/// marked with this response's nonce and whatever that script goes on to load, and nothing else; the
/// nonce is unguessable and fresh per response, so an injected tag cannot carry it.
/// </para>
/// <para>
/// The nonce is published to <see cref="HttpContext.Items"/> under <see cref="NonceItemKey"/>, where
/// the shell renderer reads it and stamps it onto the module script and its preload links. Every
/// browser in <c>docs/nfr/support-matrix.md</c> honours <c>'strict-dynamic'</c> — the oldest of them is
/// Safari 16.4, and support arrived in 15.4 — so no fallback source is needed, and adding one would
/// simply reinstate what the directive exists to remove.
/// </para>
/// <para>
/// <b>Inline styles.</b> <c>style-src</c> also governs the <c>style</c> attribute, so dropping
/// <c>'unsafe-inline'</c> means a component may not write one. The design system's
/// <c>setCssVariable</c> helper is the sanctioned way to set a custom property at run time: it writes
/// through the CSSOM, which the policy does not restrict, rather than serialising an attribute.
/// </para>
/// </remarks>
/// <param name="next">The next middleware.</param>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>The request item under which the per-response nonce is published to the renderer.</summary>
    public const string NonceItemKey = "csp-nonce";

    /// <summary>How many random bytes the nonce carries. 128 bits, per the CSP specification's floor.</summary>
    private const int NonceBytes = 16;

    /// <summary>
    /// The directives that do not depend on the nonce, in the order they are written. Kept as one
    /// constant so the policy reads as a policy rather than as string concatenation.
    /// </summary>
    private const string StaticDirectives =
        "default-src 'self'; " +
        "base-uri 'none'; " +
        "style-src 'self'; " +
        "img-src 'self' data: blob:; " +
        "media-src 'self' blob:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "worker-src 'self' blob:; " +
        "manifest-src 'self'; " +
        "object-src 'none'; " +
        "frame-src 'none'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "upgrade-insecure-requests";

    /// <summary>Builds the policy for one response.</summary>
    /// <param name="nonce">The response's nonce, already Base64 encoded.</param>
    /// <returns>The complete <c>Content-Security-Policy</c> header value.</returns>
    public static string PolicyFor(string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        return $"script-src 'nonce-{nonce}' 'strict-dynamic'; {StaticDirectives}";
    }

    /// <summary>The nonce this response was given, or null before the middleware has run.</summary>
    /// <param name="context">The request.</param>
    public static string? NonceOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(NonceItemKey, out var nonce) ? nonce as string : null;
    }

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceBytes));
        context.Items[NonceItemKey] = nonce;

        // Written when the response starts, not now. `UseExceptionHandler` re-runs the pipeline for a
        // failed request and calls `HttpResponse.Clear()` first, which discards every header written
        // eagerly. Headers set here would therefore protect every response except the error one, and a
        // 500 is precisely where a missing `X-Content-Type-Options` or a missing policy is exploitable:
        // it is the response most likely to carry an unintended body. `OnStarting` callbacks live on the
        // response feature rather than in the header collection, so `Clear()` leaves them alone.
        context.Response.OnStarting(static state =>
        {
            var (response, responseNonce) = ((HttpResponse Response, string Nonce))state;
            var headers = response.Headers;
            headers["Content-Security-Policy"] = PolicyFor(responseNonce);
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "same-origin";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            // The scan screen needs the camera; nothing else is granted.
            headers["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(), payment=()";
            return Task.CompletedTask;
        }, (context.Response, nonce));

        await next(context);
    }
}
