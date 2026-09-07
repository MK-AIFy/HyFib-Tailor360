using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Tailor360.Platform.Security.Antiforgery;

/// <summary>
/// Requires an anti-forgery token on every state-changing request, including the ones made before
/// anyone is signed in.
/// </summary>
/// <remarks>
/// <para>
/// The framework's own <c>UseAntiforgery</c> validates only endpoints that bind a form, which for a
/// JSON API means it validates nothing at all. This middleware inverts that: every request whose
/// method is not safe must present the token, and an endpoint escapes only by declaring an exemption
/// that says which non-browser caller it serves.
/// </para>
/// <para>
/// <b>Sign-in is covered deliberately.</b> The obvious reading is that a form nobody is signed in to
/// needs no cross-site protection, and it is wrong. A forged sign-in posts the <em>attacker's</em>
/// credentials into the victim's browser; the victim then works — searches customers, takes a
/// measurement, records a payment — inside an account the attacker can read at leisure. The same
/// applies to the multi-factor challenge, the passkey ceremonies and recovery, all of which end with a
/// session cookie being planted. Requiring the token on all of them costs the client one extra request
/// on start-up and closes the whole class.
/// </para>
/// </remarks>
/// <param name="next">The next middleware.</param>
/// <param name="antiforgery">The framework's token service.</param>
/// <param name="logger">Logger.</param>
public sealed class AntiforgeryEnforcementMiddleware(
    RequestDelegate next,
    IAntiforgery antiforgery,
    ILogger<AntiforgeryEnforcementMiddleware> logger)
{
    /// <summary>The problem-details code returned when the token is missing or does not validate.</summary>
    public const string ErrorCode = "security.antiforgery-token-invalid";

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (HttpMethodSafety.IsSafe(context.Request.Method))
        {
            await next(context);
            return;
        }

        var exemption = context.GetEndpoint()?.Metadata.GetMetadata<AntiforgeryExemptionMetadata>();
        if (exemption is not null)
        {
            await next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException exception)
        {
            // The exception message names which half of the token pair was wrong. That is useful to a
            // developer and useful to an attacker, so it is logged at debug and never returned.
            logger.LogWarning(
                "Refused a {Method} request to {Path}: the anti-forgery token did not validate.",
                context.Request.Method,
                context.Request.Path);
            logger.LogDebug(exception, "Anti-forgery validation detail.");

            await SecurityProblemDetails.WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                ErrorCode,
                "Security token missing or expired",
                "This request could not be verified. Reload the page and try again — signing in or "
                + "out issues a new token, and the previous one stops being accepted.");
            return;
        }

        await next(context);
    }
}
