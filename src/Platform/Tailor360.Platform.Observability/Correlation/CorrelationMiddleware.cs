using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Tailor360.Platform.Observability.Correlation;

/// <summary>
/// Establishes the correlation identifier for a request, pushes it onto the log context and echoes it
/// back to the client. Registered first in the pipeline so that even a rejected request is traceable.
/// </summary>
/// <param name="next">The next middleware.</param>
public sealed class CorrelationMiddleware(RequestDelegate next)
{
    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context, CorrelationContext correlation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(correlation);

        var supplied = context.Request.Headers[CorrelationContext.HeaderName].ToString();
        var correlationId = CorrelationContext.SanitiseOrCreate(supplied);
        correlation.Set(correlationId);

        // Written twice, deliberately.
        //
        // Once now, because the header is the only place a component that cannot see this context can
        // read the identifier from: the problem-details writers in Tailor360.Platform.Security take it
        // off the response so that a refusal carries a `correlationId` a support engineer can search
        // for, and that project does not reference this one.
        //
        // Once again when the response starts, because `UseExceptionHandler` re-runs the pipeline for a
        // failed request and calls `HttpResponse.Clear()` first, which discards every header written
        // eagerly. Without the second write the identifier would survive every response except the 500
        // — the one response whose body deliberately says nothing, and therefore the one where being
        // able to find the matching log line is the whole of what the caller is given. `OnStarting`
        // callbacks are held on the response feature rather than in the header collection, so `Clear()`
        // leaves them alone, and re-writing a header that is already there is a no-op.
        context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
        context.Response.OnStarting(static state =>
        {
            var (response, id) = ((HttpResponse Response, string Id))state;
            response.Headers[CorrelationContext.HeaderName] = id;
            return Task.CompletedTask;
        }, (context.Response, correlationId));

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
