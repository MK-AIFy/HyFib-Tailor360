using Tailor360.Modules.Billing.Contracts.Pricing;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>Draft an invoice from an order's stored calculation.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="CalculationReference">The reference the order's calculation was stored under.</param>
/// <param name="GarmentJobIds">The jobs the invoice must cover, or null for whatever the calculation priced.</param>
/// <param name="Reason">Why, or null.</param>
public sealed record CreateInvoiceRequest(Guid? OrderId, string? CalculationReference, IReadOnlyList<Guid>? GarmentJobIds, string? Reason);

/// <summary>Replace a draft's lines by pricing them afresh.</summary>
/// <param name="On">The branch-local date the price is asked for.</param>
/// <param name="PlaceOfSupplyStateCode">The two-digit state code of the place of supply.</param>
/// <param name="Lines">The lines, each keyed by the garment job it charges for.</param>
/// <param name="Reason">Why, or null.</param>
public sealed record RepriceInvoiceRequest(
    DateOnly? On,
    string? PlaceOfSupplyStateCode,
    IReadOnlyList<PricingLineRequestPayload?>? Lines,
    string? Reason)
{
    /// <summary>The lines as the contract reads them; an omitted value becomes one the engine refuses.</summary>
    public IReadOnlyList<PricingLineRequest> ToLines()
        => [.. (Lines ?? []).Select(line => line?.ToLine() ?? new PricingLineRequest(string.Empty, string.Empty, 0m, [], null, null))];
}
