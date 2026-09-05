using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tailor360.Modules.Integration.Api;

/// <summary>
/// The Integration module's HTTP surface, covering the integration event relay, webhooks, provider adapters and the accounting export.
/// Every endpoint added here must declare an authorisation policy through
/// <c>RequirePermission</c> or an explicit anonymous justification (architecture rule ARCH-007).
/// </summary>
public static class IntegrationEndpoints
{
    /// <summary>The route prefix for this module. The major API version is part of the path (D18).</summary>
    public const string GroupPrefix = "/api/v1/integration";

    /// <summary>The OpenAPI tag applied to this module's operations.</summary>
    public const string OpenApiTag = "Integration";

    /// <summary>Maps the module's endpoint group.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        _ = endpoints.MapGroup(GroupPrefix)
            .WithTags(OpenApiTag);

        return endpoints;
    }
}
