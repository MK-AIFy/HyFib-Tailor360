using Tailor360.Modules.Billing.Contracts.Pricing;
using Tailor360.Modules.Billing.Domain.Invoicing;

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

/// <summary>Post a credit or debit note against a posted invoice.</summary>
/// <param name="Lines">The garment jobs of the invoice and the taxable value each relieves or adds.</param>
/// <param name="Reason">Why; required.</param>
public sealed record PostAdjustmentNoteRequest(IReadOnlyList<AdjustmentNoteLineRequestPayload?>? Lines, string? Reason)
{
    /// <summary>The lines as the domain reads them.</summary>
    public IReadOnlyList<AdjustmentNoteLineRequest> ToLines()
        => [.. (Lines ?? []).Select(line => new AdjustmentNoteLineRequest(line?.GarmentJobId ?? Guid.Empty, line?.TaxableValue ?? 0m))];
}

/// <summary>One line of a note request.</summary>
/// <param name="GarmentJobId">The garment job of the invoice line.</param>
/// <param name="TaxableValue">The taxable value moved, positive, to the paisa.</param>
public sealed record AdjustmentNoteLineRequestPayload(Guid? GarmentJobId, decimal? TaxableValue);
