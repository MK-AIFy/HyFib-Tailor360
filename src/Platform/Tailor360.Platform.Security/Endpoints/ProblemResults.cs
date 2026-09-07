using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// Builds the RFC 9457 problem document every endpoint answers a refusal with, in the envelope
/// <c>docs/architecture/conventions.md</c> section 4.3 fixes: a <c>urn:tailor360:problem:</c> type, the
/// stable <c>code</c>, the instance, the correlation identifier and whether retrying can help.
/// </summary>
/// <remarks>
/// <para>
/// <b>No exception, message or stack trace ever passes through here.</b> Every argument is a value the
/// caller chose: a code from a published registry and a sentence written for the person reading the
/// screen. A database message or an exception string would describe the shape of the system to whoever
/// asked, and is never what somebody standing at a counter needed to know.
/// </para>
/// <para>
/// It is in the platform rather than in a module because the envelope is a contract with the client:
/// the progressive web application branches on <c>code</c> and on <c>retryable</c>, and two modules
/// spelling the same failure two ways would each need their own handling in the client.
/// </para>
/// </remarks>
public static class ProblemResults
{
    /// <summary>The URN namespace every problem type is published under.</summary>
    public const string TypePrefix = "urn:tailor360:problem:";

    /// <summary>The header the correlation middleware has already placed on the response.</summary>
    private const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>Builds a problem document.</summary>
    /// <param name="context">The request, for the instance path and the correlation identifier.</param>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="code">The stable, dotted error code, for example <c>idempotency.key-reused</c>.</param>
    /// <param name="title">A short title, the same for every occurrence of this code.</param>
    /// <param name="detail">What happened and what to do about it, written for a person.</param>
    /// <param name="retryable">True when repeating the same request unchanged may succeed.</param>
    /// <param name="extensions">Extra members, such as <c>currentVersion</c> on a conflict.</param>
    public static IResult From(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        string detail,
        bool retryable = false,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var members = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["retryable"] = retryable,
        };

        var correlationId = context.Response.Headers[CorrelationHeader].ToString();
        if (correlationId.Length > 0)
        {
            members["correlationId"] = correlationId;
        }

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                members[key] = value;
            }
        }

        return Results.Problem(
            detail: detail,
            instance: context.Request.Path,
            statusCode: statusCode,
            title: title,
            type: TypePrefix + code,
            extensions: members);
    }

    /// <summary>
    /// Builds a problem document that also tells the client how long to wait, in the header a client
    /// obeys and in a member a person can be shown.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="code">The stable error code.</param>
    /// <param name="title">A short title.</param>
    /// <param name="detail">What happened and what to do about it.</param>
    /// <param name="retryAfter">How long to wait; rounded up to a whole second, and never below one.</param>
    public static IResult RetryAfter(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        string detail,
        TimeSpan retryAfter)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seconds = (int)Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        return From(
            context,
            statusCode,
            code,
            title,
            detail,
            retryable: true,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["retryAfterSeconds"] = seconds,
            });
    }
}
