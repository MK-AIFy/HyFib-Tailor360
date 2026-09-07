using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tailor360.Platform.Security.Antiforgery;

/// <summary>
/// Refuses a state-changing request that a browser says came from somewhere else. It runs before
/// anything expensive, so a cross-site attempt costs a header comparison rather than a database
/// lookup.
/// </summary>
/// <remarks>
/// <para>
/// Two independent signals are checked. <c>Sec-Fetch-Site</c> is set by the browser itself and cannot
/// be altered by the page making the request, which makes it the stronger of the two; <c>Origin</c> is
/// also browser-set on state-changing requests and covers the browsers that do not send fetch metadata.
/// A request that declares neither is not, by itself, evidence of anything — the command-line tool and
/// the probes send neither — so it passes here and meets anti-forgery validation instead.
/// </para>
/// <para>
/// <c>same-site</c> is refused as well as <c>cross-site</c>. The session cookie carries
/// <c>SameSite=Lax</c>, which a browser considers satisfied by any host under the same registrable
/// domain, so a compromised sibling host would otherwise be able to make authenticated calls. The
/// <c>__Host-</c> prefix stops such a host from <em>setting</em> the cookie; this stops it from
/// <em>using</em> one.
/// </para>
/// </remarks>
/// <param name="next">The next middleware.</param>
/// <param name="options">Which origins are acceptable.</param>
/// <param name="logger">Logger.</param>
public sealed class OriginValidationMiddleware(
    RequestDelegate next,
    IOptions<RequestOriginOptions> options,
    ILogger<OriginValidationMiddleware> logger)
{
    /// <summary>The problem-details code returned when the check refuses a request.</summary>
    public const string ErrorCode = "security.cross-site-request";

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (HttpMethodSafety.IsSafe(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (Evaluate(context.Request) is { } refusal)
        {
            // The refusal names the signal, never the value: a hostile Origin header is attacker-
            // controlled text and does not belong in a log line.
            logger.LogWarning(
                "Refused a {Method} request to {Path}: {Refusal}.",
                context.Request.Method,
                context.Request.Path,
                refusal);

            await SecurityProblemDetails.WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                ErrorCode,
                "Cross-site request refused",
                "This request appears to come from another site and was refused. Open the "
                + "application directly and try again.");
            return;
        }

        await next(context);
    }

    private string? Evaluate(HttpRequest request)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Length > 0
            && !string.Equals(fetchSite, "same-origin", StringComparison.Ordinal)
            && !string.Equals(fetchSite, "none", StringComparison.Ordinal))
        {
            return "the browser reported the request as not same-origin";
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsAllowed(origin, request))
        {
            return "the declared origin is not this application's origin";
        }

        if (options.Value.RequireDeclaredOrigin && fetchSite.Length == 0 && origin.Length == 0)
        {
            return "the request declared neither an origin nor fetch metadata";
        }

        return null;
    }

    private bool IsAllowed(string origin, HttpRequest request)
    {
        // "null" is what a sandboxed frame, a data: document and a redirected cross-origin request all
        // send. None of them is the client, and treating the literal as a host would be a parse away
        // from allowing it.
        if (string.Equals(origin, "null", StringComparison.Ordinal))
        {
            return false;
        }

        var expected = $"{request.Scheme}://{request.Host.Value}";
        if (string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var allowed in options.Value.AdditionalAllowedOrigins)
        {
            if (string.Equals(origin, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
