using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Api;

/// <summary>
/// Turns a domain or application <see cref="Error"/> into the RFC 9457 body conventions section 4.3
/// fixes: a <c>urn:tailor360:problem:</c> type, the stable code, the correlation identifier, and field
/// errors when the failure is about a field.
/// </summary>
/// <remarks>
/// <para>
/// <b>No exception ever reaches a caller through here.</b> The input is a value the handler chose to
/// return, and its message was written to be read by whoever is looking at the screen. A stack trace, a
/// database message or an exception string would leak the shape of the system to an anonymous caller
/// and is never what a person at a counter needed to know.
/// </para>
/// <para>
/// <b>Authentication failures answer 401, not 403.</b> The classification a handler returns says what
/// kind of failure it was; the status code says what the caller should do about it. "Your credentials
/// did not authenticate" is an instruction to authenticate again, which is 401, whereas 403 tells a
/// client that retrying with the same identity is pointless. Getting that backwards makes a mistyped
/// password look permanent.
/// </para>
/// </remarks>
public static class Problems
{
    /// <summary>The header the correlation middleware has already placed on the response.</summary>
    private const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>Answers a failed result.</summary>
    /// <param name="error">The failure the handler returned.</param>
    /// <param name="context">The request, for the instance path and the correlation identifier.</param>
    /// <param name="statusOverride">
    /// The status to answer with, where the classification alone would send the wrong instruction —
    /// a refused credential, which is 401.
    /// </param>
    public static IResult From(Error error, HttpContext context, int? statusOverride = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var status = statusOverride ?? StatusFor(error.Type);
        var extensions = Extensions(error, context);

        if (error.Type is ErrorType.Validation && error.Target is { Length: > 0 } field)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [error.Message] },
                detail: error.Message,
                instance: context.Request.Path,
                statusCode: status,
                title: TitleFor(status),
                type: TypeFor(error),
                extensions: extensions);
        }

        return Results.Problem(
            detail: error.Message,
            instance: context.Request.Path,
            statusCode: status,
            title: TitleFor(status),
            type: TypeFor(error),
            extensions: extensions);
    }

    /// <summary>
    /// Answers a request the throttle refused, naming how long to wait both in the header a client
    /// obeys and in the body a person reads.
    /// </summary>
    public static IResult TooManyAttempts(HttpContext context, TimeSpan retryAfter, string code, string detail)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seconds = (int)Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["retryable"] = true,
            ["retryAfterSeconds"] = seconds,
        };

        AddCorrelation(extensions, context);

        return Results.Problem(
            detail: detail,
            instance: context.Request.Path,
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too many attempts",
            type: $"urn:tailor360:problem:{code}",
            extensions: extensions);
    }

    private static Dictionary<string, object?> Extensions(Error error, HttpContext context)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = error.Code,
            ["retryable"] = error.Type is ErrorType.Unavailable,
        };

        AddCorrelation(extensions, context);
        return extensions;
    }

    private static void AddCorrelation(Dictionary<string, object?> extensions, HttpContext context)
    {
        var correlationId = context.Response.Headers[CorrelationHeader].ToString();
        if (correlationId.Length > 0)
        {
            extensions["correlationId"] = correlationId;
        }
    }

    private static string TypeFor(Error error)
        => $"urn:tailor360:problem:{(error.Code.Length > 0 ? error.Code : "unexpected")}";

    private static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unauthenticated => StatusCodes.Status401Unauthorized,
        ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "That request could not be accepted",
        StatusCodes.Status401Unauthorized => "Sign in to continue",
        StatusCodes.Status403Forbidden => "Not allowed",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "That conflicts with the current state",
        StatusCodes.Status412PreconditionFailed => "A precondition was not met",
        StatusCodes.Status503ServiceUnavailable => "Temporarily unavailable",
        _ => "Something went wrong",
    };
}
