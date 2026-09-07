using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tailor360.Platform.Security.Antiforgery;

/// <summary>
/// Writes the RFC 9457 body the security middleware returns when it refuses a request, in the envelope
/// conventions section 4.3 fixes: a <c>urn:tailor360:problem:</c> type, the stable code, the instance
/// and the correlation identifier.
/// </summary>
internal static class SecurityProblemDetails
{
    /// <summary>The header the correlation middleware has already put on the response.</summary>
    private const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>
    /// Writes a refusal. The detail is written for the person who will read it in the client and says
    /// what to do next; it never names the header that failed, the value it carried or anything about
    /// the caller, because a refusal is exactly the moment not to explain how to pass.
    /// </summary>
    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        var correlationId = context.Response.Headers[CorrelationHeader].ToString();

        context.Response.Clear();
        context.Response.StatusCode = statusCode;

        // Clearing the response drops the correlation header with everything else, and a refusal a
        // support engineer cannot tie to a log line is a refusal nobody can explain.
        if (correlationId.Length > 0)
        {
            context.Response.Headers[CorrelationHeader] = correlationId;
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"urn:tailor360:problem:{code}",
            Instance = context.Request.Path,
        };

        problem.Extensions["code"] = code;
        problem.Extensions["retryable"] = false;

        if (correlationId.Length > 0)
        {
            problem.Extensions["correlationId"] = correlationId;
        }

        // The provider is absent only outside a real pipeline. Falling back to a bodiless refusal
        // there is right: the status code is the answer, and a null reference at the moment of
        // refusing a request would turn a 403 into a 500.
        var service = context.RequestServices?.GetService<IProblemDetailsService>();
        if (service is not null
            && await service.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problem,
            }))
        {
            return;
        }

        // A host that registered no problem-details service still has to receive a refusal rather than
        // an empty body, so the status code is the answer and the body is omitted.
        await context.Response.CompleteAsync();
    }
}
