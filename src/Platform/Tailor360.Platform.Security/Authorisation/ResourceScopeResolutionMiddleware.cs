using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Loads the resource an endpoint names, before the authorisation handlers decide anything.
/// </summary>
/// <remarks>
/// <para>
/// It is middleware and not an endpoint filter, although the design calls it a filter, because an
/// endpoint filter runs <em>after</em> the authorisation middleware has already allowed or refused the
/// request. Placing the load between routing — which is what makes the endpoint and its route values
/// available — and authorisation is the only position in the ASP.NET Core pipeline where the resource
/// can be known before the decision is taken. The name says what it does; the position is the design.
/// </para>
/// <para>
/// It runs once per request and answers once. A request whose endpoint names no resource is marked as
/// such rather than left alone, so that a handler can tell "no resource to check" from "the step that
/// checks never ran" — the second is a defect in the host and is refused.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
public sealed class ResourceScopeResolutionMiddleware(RequestDelegate next)
{
    /// <summary>Resolves the resource for this request, if the endpoint names one.</summary>
    public async Task InvokeAsync(HttpContext context, ResourceScopeContext resourceScope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resourceScope);

        var declaration = context.GetEndpoint()?.Metadata.GetMetadata<ResourceScopeMetadata>();
        if (declaration is null)
        {
            resourceScope.SetNotRequired();
            await next(context);
            return;
        }

        if (!context.Request.RouteValues.TryGetValue(declaration.RouteValueName, out var raw))
        {
            // The endpoint names a route parameter its own template does not have. That is a defect in
            // the endpoint, not a refusable request, and it must fail loudly the first time it is run
            // rather than silently answering "not found" for every caller.
            throw new InvalidOperationException(
                $"The endpoint declares resource '{declaration.ResourceKind}' at route value "
                + $"'{declaration.RouteValueName}', which its route template does not define.");
        }

        var resolver = Resolver(context, declaration.ResourceKind);

        if (!Guid.TryParse(raw as string ?? raw?.ToString(), out var resourceId))
        {
            // A malformed identifier is a caller's mistake, and it is answered exactly as an identifier
            // belonging to somebody else is.
            resourceScope.SetNotFound();
            await next(context);
            return;
        }

        var scope = await resolver.ResolveAsync(resourceId, context.RequestAborted);
        if (scope is null)
        {
            resourceScope.SetNotFound();
        }
        else
        {
            resourceScope.SetResolved(scope);
        }

        await next(context);
    }

    private static IResourceScopeResolver Resolver(HttpContext context, string kind)
    {
        var resolvers = context.RequestServices.GetServices<IResourceScopeResolver>();
        var resolver = resolvers.FirstOrDefault(
            candidate => string.Equals(candidate.ResourceKind, kind, StringComparison.Ordinal));

        return resolver ?? throw new InvalidOperationException(
            $"No resource scope resolver is registered for '{kind}'. The module that owns the resource "
            + "registers one in its service-collection extension.");
    }
}
