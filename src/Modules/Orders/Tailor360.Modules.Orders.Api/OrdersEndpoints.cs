using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tailor360.Modules.Orders.Api;

/// <summary>
/// The Orders module's HTTP surface, covering estimates, orders, garment jobs, snapshots, the production workflow, QC and alterations.
/// Every endpoint added here must declare an authorisation policy through
/// <c>RequirePermission</c> or an explicit anonymous justification (architecture rule ARCH-007).
/// </summary>
public static class OrdersEndpoints
{
    /// <summary>The route prefix for this module. The major API version is part of the path (D18).</summary>
    public const string GroupPrefix = "/api/v1/orders";

    /// <summary>The OpenAPI tag applied to this module's operations.</summary>
    public const string OpenApiTag = "Orders";

    /// <summary>Maps the module's endpoint group.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        _ = endpoints.MapGroup(GroupPrefix)
            .WithTags(OpenApiTag);

        return endpoints;
    }
}
