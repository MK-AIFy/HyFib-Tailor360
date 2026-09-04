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

        context.Response.Headers[CorrelationContext.HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
