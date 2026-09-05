using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Platform.Security.Antiforgery;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Web.Endpoints;

/// <summary>
/// Hands the client the request half of the anti-forgery token pair. It belongs to the
/// backend-for-frontend rather than to a module: the token is a property of the browser session with
/// this host, not of anything the Identity module owns.
/// </summary>
public static class AntiForgeryEndpoints
{
    /// <summary>The route the client fetches before its first state-changing request.</summary>
    public const string Path = "/api/v1/antiforgery";

    /// <summary>
    /// Maps the token endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The call sets the cookie half — <c>__Host-t360.csrf</c>, <c>HttpOnly</c> — and returns the
    /// request half in the body. Script keeps that value in memory and sends it in the
    /// <c>X-CSRF-Token</c> header; it is deliberately the only piece of security material script can
    /// read, and it is useless without the cookie the script cannot read.
    /// </para>
    /// <para>
    /// <b>The response must never be cached.</b> A cached token is either stale, which presents as
    /// unexplained refusals, or shared, which is worse: a proxy or a service worker that served one
    /// person's token to another would hand out half of a valid pair. Hence <c>no-store</c> here, and
    /// the client's service worker excludes this path from its caches.
    /// </para>
    /// <para>
    /// <b>The token is bound to the signed-in account</b>, so signing in, signing out and switching
    /// account all invalidate the one in hand. The client refetches after any of them, and treats a
    /// <c>security.antiforgery-token-invalid</c> refusal as "fetch a new token and retry once".
    /// </para>
    /// <para>
    /// <b>This endpoint requires the request to be HTTPS</b>, because the cookie half is configured
    /// <c>Secure</c> and the framework refuses to issue a <c>Secure</c> cookie over a plain request
    /// rather than issuing one the browser will silently discard. Behind the reverse proxy that is
    /// satisfied by <c>X-Forwarded-Proto</c>, which means this endpoint is the first thing to fail —
    /// loudly, with a 500 — if <c>ForwardedHeaders:KnownProxies</c> or <c>KnownNetworks</c> does not
    /// cover the proxy. That is the intended direction to fail in: the alternative is a deployment that
    /// serves a session over plain HTTP and looks healthy.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The route builder.</param>
    public static IEndpointRouteBuilder MapAntiForgeryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(Path, (HttpContext context, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(context);

                context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                context.Response.Headers.Pragma = "no-cache";

                return Results.Ok(new AntiForgeryTokenResponse(
                    tokens.RequestToken ?? string.Empty,
                    AntiforgeryDefaults.HeaderName));
            })
            .AllowAnonymousWithJustification(
                "The anti-forgery token pair has to exist before anyone can sign in, because the "
                + "sign-in request itself must carry the token: a sign-in forged from another origin "
                + "plants the attacker's account in the victim's browser and everything the victim "
                + "then does is recorded against it. The response reveals nothing about the caller — "
                + "the request token is meaningless without the paired cookie, which script cannot "
                + "read — and the endpoint changes no state, so it is safe to reach unauthenticated.",
                "#23, docs/security/threat-models/authentication.md")
            .RequireRateLimiting(RateLimitPolicyNames.DefaultIp)
            .WithName("GetAntiForgeryToken")
            .WithTags("Platform");

        return endpoints;
    }
}

/// <summary>The request half of the anti-forgery token pair, and the header to send it in.</summary>
/// <param name="Token">
/// The request token. Script holds it in memory only: writing it to <c>localStorage</c> would survive
/// a sign-out and outlive the pair it belongs to.
/// </param>
/// <param name="HeaderName">The header the client must send the token in.</param>
public sealed record AntiForgeryTokenResponse(string Token, string HeaderName);
