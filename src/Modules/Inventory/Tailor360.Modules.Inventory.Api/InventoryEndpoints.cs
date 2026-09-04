using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tailor360.Modules.Inventory.Api;

/// <summary>
/// The Inventory module's HTTP surface, covering items, units, suppliers, locations, the stock ledger, reservations, stocktakes and alerts.
/// Every endpoint added here must declare an authorisation policy through
/// <c>RequirePermission</c> or an explicit anonymous justification (architecture rule ARCH-007).
/// </summary>
public static class InventoryEndpoints
{
    /// <summary>The route prefix for this module. The major API version is part of the path (D18).</summary>
    public const string GroupPrefix = "/api/v1/inventory";

    /// <summary>The OpenAPI tag applied to this module's operations.</summary>
    public const string OpenApiTag = "Inventory";

    /// <summary>Maps the module's endpoint group.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        _ = endpoints.MapGroup(GroupPrefix)
            .WithTags(OpenApiTag);

        return endpoints;
    }
}
