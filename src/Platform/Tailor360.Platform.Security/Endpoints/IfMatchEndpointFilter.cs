using Microsoft.AspNetCore.Http;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// Refuses an update that did not say which version it was made against, before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// It answers only the two questions that can be answered without loading the aggregate: was the header
/// there, and was it a strong entity tag. Whether the version is <em>current</em> is a question only the
/// handler can answer, and it answers it with
/// <see cref="ConcurrencyResults.CheckIfMatch(HttpContext, EntityTag, string, string)"/>.
/// </para>
/// <para>
/// The point of refusing early is that a client which forgot the header is a client whose next write
/// would overwrite somebody's edit, and it should find that out on the first attempt rather than the
/// first time two people are working at once.
/// </para>
/// </remarks>
public sealed class IfMatchEndpointFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var header = http.Request.Headers.IfMatch.ToString();

        if (header.Length == 0)
        {
            return ConcurrencyResults.PreconditionMissing(http);
        }

        if (!EntityTag.TryParse(header, out _))
        {
            return ProblemResults.From(
                http,
                StatusCodes.Status400BadRequest,
                ConcurrencyProblems.PreconditionMalformed,
                "That request could not be accepted",
                "The If-Match header must be the ETag this record was last read with, in double quotes.");
        }

        return await next(context);
    }
}

/// <summary>
/// Marks an endpoint as requiring a concurrency precondition, for the endpoint inventory and the
/// generated specification.
/// </summary>
public sealed record IfMatchRequiredMetadata;
