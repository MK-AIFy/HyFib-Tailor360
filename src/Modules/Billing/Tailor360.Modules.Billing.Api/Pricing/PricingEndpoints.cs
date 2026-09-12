using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Billing.Api.Payloads;
using Tailor360.Modules.Billing.Application.Pricing;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Api.Pricing;

/// <summary>The pricing preview (#147): the engine run against a named version, for an administrator's eyes.</summary>
public static class PricingEndpoints
{
    /// <summary>The preview was run.</summary>
    public const string PreviewedAction = "billing.pricing.previewed";

    /// <summary>Maps the route onto the Billing group.</summary>
    public static RouteGroupBuilder MapPricingEndpoints(this RouteGroupBuilder billing)
    {
        ArgumentNullException.ThrowIfNull(billing);

        billing.MapPost("/pricing/preview", async Task<IResult> (
                PricingPreviewRequest request,
                HttpContext context,
                PricingService pricing,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                if (request.PriceListVersionId is not { } versionId)
                {
                    return Problems.From(Domain.BillingErrors.Required("priceListVersionId"), context);
                }

                // A discount or an override sent without its number is a missing field, not a malformed one.
                foreach (var line in request.Lines ?? [])
                {
                    if (line?.Discount is { Value: null })
                    {
                        return Problems.From(Domain.BillingErrors.Required($"lines[{line.LineKey}].discount.value"), context);
                    }

                    if (line?.Override is { Rate: null })
                    {
                        return Problems.From(Domain.BillingErrors.Required($"lines[{line.LineKey}].override.rate"), context);
                    }
                }

                var result = await pricing.PreviewAsync(
                    request.ToRequest(caller.Context.OrganisationId), versionId, request.TaxConfigurationVersionId, cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(PricingResultPayload.From(result.Value));
            })
            .Produces<PricingResultPayload>(StatusCodes.Status200OK)
            .WithName("PreviewPricing")
            .WithSummary("Price lines against a named price-list version, draft or published, without storing the result.")
            .WithDescription(
                "The same engine an order is priced by, run against the version an administrator names so that the "
                + "effect of a change is seen before it is published. Nothing is stored; an override beyond the "
                + "version's threshold or a discount beyond its rule's counter maximum still needs billing.override_price.")
            .RequirePermission(BillingPermissions.ManagePriceLists, BranchScope.Organisation)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PreviewedAction)
            .WithRequestTimeout(RequestTimeoutPolicies.Command);

        return billing;
    }
}
