using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Api;

/// <summary>
/// Turns a domain or application <see cref="Error"/> into the RFC 9457 body conventions section 4.3
/// fixes: a <c>urn:tailor360:problem:</c> type, the stable code, the correlation identifier, and field
/// errors when the failure is about a field.
/// </summary>
/// <remarks>
/// A copy of the Catalog module's class, and deliberately a copy rather than a shared
/// helper: a module may not reference another module's <c>Api</c> project (ARCH-004), and the platform
/// publishes <c>ProblemResults</c>, which takes a status and a code rather than an
/// <see cref="Error"/>. The status and title tables below are identical to theirs on purpose — two
/// modules answering one classification two ways is the drift this comment exists to make visible if
/// it ever happens. <see cref="FromFindings"/> is kept, because a version refused at publish has many
/// reasons rather than one.
/// </remarks>
/// <remarks>
/// <b>No exception ever reaches a caller through here.</b> The input is a value the handler chose to
/// return, and its message was written to be read by whoever is looking at the screen. A stack trace,
/// a raw exception message, an amount, a GSTIN or a customer's name never appears in a problem detail
/// (<c>docs/nfr/data-classification.md</c>): the field is named, the value is not.
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

    /// <summary>Answers a publication refused by its checks, with every finding in the body.</summary>
    /// <remarks>
    /// One refusal carrying every finding, because an administrator fixing one thing at a time and
    /// re-submitting is a much worse afternoon than a single report naming all of them. Errors with a
    /// target become field errors; the whole list, warnings included, rides in <c>findings</c>.
    /// </remarks>
    /// <param name="error">The refusal.</param>
    /// <param name="findings">Everything the checks found.</param>
    /// <param name="context">The request.</param>
    /// <returns>The problem detail.</returns>
    public static IResult FromFindings(
        Error error,
        IReadOnlyList<BillingFindingPayload> findings,
        HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(context);

        var fields = findings
            .Where(finding => finding.Severity == nameof(BillingFindingSeverity.Error))
            .Where(finding => finding.Target is { Length: > 0 })
            .GroupBy(finding => finding.Target!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(finding => finding.Message).ToArray(),
                StringComparer.Ordinal);

        var extensions = Extensions(error, context);
        extensions["findings"] = findings;

        return Results.ValidationProblem(
            fields,
            detail: error.Message,
            instance: context.Request.Path,
            statusCode: StatusFor(error.Type),
            title: TitleFor(StatusFor(error.Type)),
            type: TypeFor(error),
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
