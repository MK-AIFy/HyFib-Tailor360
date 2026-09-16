using Tailor360.Platform.Security.Antiforgery;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Web.Telemetry;

/// <summary>
/// Refuses a request that declares neither <c>Origin</c> nor <c>Sec-Fetch-Site</c> — this route's own
/// compensating control for being anti-forgery-exempt.
/// </summary>
/// <remarks>
/// <para>
/// <c>OriginValidationMiddleware</c> already refuses a request whose declared origin is wrong, for every
/// non-safe request in the pipeline; that check is unconditional and needs nothing more here. What it
/// does <em>not</em> refuse — by design, since <c>RequestOriginOptions.RequireDeclaredOrigin</c> is
/// <c>false</c> — is a request that declares neither signal at all, because the command-line tool and
/// the health probes are exactly such requests and anti-forgery validation normally stands behind that
/// gap. This route is anti-forgery-exempt (a <c>sendBeacon</c> call cannot carry the token), so the gap
/// is open here unless something else closes it — this filter is that something else, applied to this
/// one route rather than to <c>RequestOriginOptions</c> globally, which would also refuse the tool and
/// the probes it exists for.
/// </para>
/// <para>
/// Reuses <see cref="OriginValidationMiddleware.ErrorCode"/> and the shared problem-details envelope
/// through <see cref="ProblemResults"/>, so a caller sees one wire shape for "refused as cross-site"
/// wherever it happens, not two.
/// </para>
/// </remarks>
public sealed class DeclaredOriginRequiredFilter : IEndpointFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.HttpContext.Request;
        var declaresOrigin = request.Headers.Origin.Count > 0;
        var declaresFetchSite = request.Headers["Sec-Fetch-Site"].Count > 0;

        if (!declaresOrigin && !declaresFetchSite)
        {
            return ValueTask.FromResult<object?>(ProblemResults.From(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                OriginValidationMiddleware.ErrorCode,
                "Cross-site request refused",
                "This request appears to come from another site and was refused. Open the application " +
                "directly and try again."));
        }

        return next(context);
    }
}
