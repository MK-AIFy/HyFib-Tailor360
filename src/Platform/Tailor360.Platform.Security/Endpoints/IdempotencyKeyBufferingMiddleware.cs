using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// Lets the idempotency filter read a request body that model binding has already consumed.
/// </summary>
/// <remarks>
/// <para>
/// A minimal-API endpoint filter runs <em>after</em> its parameters are bound, so by the time the
/// idempotency filter is asked to fingerprint the request, the body stream has been read to its end and
/// cannot be rewound. This step turns buffering on before routing so that it can be.
/// </para>
/// <para>
/// It does that only for requests that carry an <c>Idempotency-Key</c>, which is a small and
/// self-selecting set: buffering every request would spool uploads and imports for no reason. The size
/// of what may be buffered is bounded by the host's request-size limit, which is where a body limit
/// belongs.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
public sealed class IdempotencyKeyBufferingMiddleware(RequestDelegate next)
{
    /// <summary>Enables rewinding for requests that claim to be idempotent.</summary>
    /// <param name="context">The request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Headers.ContainsKey(IdempotencyHeaders.Key) && !context.Request.Body.CanSeek)
        {
            context.Request.EnableBuffering();
        }

        await next(context);
    }
}

/// <summary>Places the idempotency buffering step in the request pipeline.</summary>
public static class IdempotencyApplicationBuilderExtensions
{
    /// <summary>
    /// Makes request bodies re-readable for idempotent commands. Place after <c>UseRouting</c> and before
    /// the endpoints run; a host that omits it does not quietly lose the guarantee — every idempotent
    /// endpoint with a body refuses, and the refusal is logged as the defect it is.
    /// </summary>
    /// <param name="app">The application builder.</param>
    public static IApplicationBuilder UseTailor360IdempotencyKeys(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<IdempotencyKeyBufferingMiddleware>();
    }
}
