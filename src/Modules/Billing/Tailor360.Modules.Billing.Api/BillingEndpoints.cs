using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Registrations;
using Tailor360.Modules.Billing.Api.Tax;

namespace Tailor360.Modules.Billing.Api;

/// <summary>
/// The Billing module's HTTP surface, covering the pricing and tax engine, invoices, payments, receipts and cashier reconciliation.
/// Every endpoint added here must declare an authorisation policy through
/// <c>RequirePermission</c> or an explicit anonymous justification (architecture rule ARCH-007).
/// </summary>
public static class BillingEndpoints
{
    /// <summary>The route prefix for this module. The major API version is part of the path (D18).</summary>
    public const string GroupPrefix = "/api/v1/billing";

    /// <summary>The OpenAPI tag applied to this module's operations.</summary>
    public const string OpenApiTag = "Billing";

    /// <summary>Maps the module's endpoint group.</summary>
    /// <param name="endpoints">The route builder supplied by the host.</param>
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGroup(GroupPrefix)
            .WithTags(OpenApiTag)
            .MapTaxConfigurationEndpoints()
            .MapGstRegistrationEndpoints();

        return endpoints;
    }
}
