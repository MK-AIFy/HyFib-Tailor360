using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Customers.Api.Consent;
using Tailor360.Modules.Customers.Api.Customers;

namespace Tailor360.Modules.Customers.Api;

/// <summary>
/// The Customers module's HTTP surface, covering customers, consent, communication preferences, measurement templates and measurement versions.
/// Every endpoint added here must declare an authorisation policy through
/// <c>RequirePermission</c> or an explicit anonymous justification (architecture rule ARCH-007).
/// </summary>
public static class CustomersEndpoints
{
    /// <summary>The route prefix for this module. The major API version is part of the path (D18).</summary>
    public const string GroupPrefix = "/api/v1/customers";

    /// <summary>The OpenAPI tag applied to this module's operations.</summary>
    public const string OpenApiTag = "Customers";

    /// <summary>Maps the module's endpoint group.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapCustomersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGroup(GroupPrefix)
            .WithTags(OpenApiTag)
            .MapCustomerEndpoints()
            .MapConsentEndpoints();

        return endpoints;
    }
}
