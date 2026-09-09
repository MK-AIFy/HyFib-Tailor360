using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Catalog.Api.Catalogue;

namespace Tailor360.Modules.Catalog.Api;

/// <summary>
/// The Catalog module's HTTP surface, covering stitching categories, service types, design option groups and QC checklist templates.
/// Every endpoint added here must declare an authorisation policy through
/// <c>RequirePermission</c> or an explicit anonymous justification (architecture rule ARCH-007).
/// </summary>
public static class CatalogEndpoints
{
    /// <summary>The route prefix for this module. The major API version is part of the path (D18).</summary>
    public const string GroupPrefix = "/api/v1/catalog";

    /// <summary>The OpenAPI tag applied to this module's operations.</summary>
    public const string OpenApiTag = "Catalog";

    /// <summary>Maps the module's endpoint group.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGroup(GroupPrefix)
            .WithTags(OpenApiTag)
            .MapCurrentCatalogEndpoints()
            .MapCatalogVersionEndpoints();

        return endpoints;
    }
}
