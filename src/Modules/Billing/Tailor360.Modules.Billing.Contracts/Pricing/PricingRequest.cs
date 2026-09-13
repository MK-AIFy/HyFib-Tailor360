namespace Tailor360.Modules.Billing.Contracts.Pricing;

/// <summary>What a caller asks to have priced.</summary>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch the work is taken at; its published price-list version and its registration price the request.</param>
/// <param name="On">The branch-local date the price is asked for; decides which versions are in force.</param>
/// <param name="PlaceOfSupplyStateCode">The two-digit GST state code of the place of supply; decides intra- or inter-state.</param>
/// <param name="Reference">
/// The caller's key for the calculation, or null to price without storing. A stored calculation is
/// answered again under the same reference whatever has been published since; two different
/// calculations of one thing — an estimate, then the confirmation — carry two references.
/// </param>
/// <param name="Lines">The lines, each under the caller's own key.</param>
public sealed record PricingRequest(
    Guid OrganisationId,
    Guid BranchId,
    DateOnly On,
    string PlaceOfSupplyStateCode,
    string? Reference,
    IReadOnlyList<PricingLineRequest> Lines);

/// <summary>One line to price.</summary>
/// <param name="LineKey">The caller's key, unique within the request; the line comes back under it.</param>
/// <param name="ItemCode">The price-list item the line is for.</param>
/// <param name="Quantity">How many; positive.</param>
/// <param name="SurchargeItemCodes">
/// Items charged on top of the base, per unit — a design option's price impact, a finish. Each must
/// carry the base item's tax code: anything taxed differently is its own line.
/// </param>
/// <param name="Discount">A discount under one of the version's rules, or null.</param>
/// <param name="Override">A rate in place of the catalogue rate, with the reason, or null.</param>
public sealed record PricingLineRequest(
    string LineKey,
    string ItemCode,
    decimal Quantity,
    IReadOnlyList<string> SurchargeItemCodes,
    PricingDiscountRequest? Discount,
    PricingOverrideRequest? Override);

/// <summary>A discount on a line.</summary>
/// <param name="RuleCode">The discount rule of the price-list version.</param>
/// <param name="Value">A percentage of the line for a percentage rule, an amount off it for an amount rule.</param>
/// <param name="Reason">Why, required when the value exceeds what the counter may give on its own.</param>
public sealed record PricingDiscountRequest(string RuleCode, decimal Value, string? Reason);

/// <summary>A rate in place of the catalogue rate.</summary>
/// <param name="Rate">The rate per unit to charge instead.</param>
/// <param name="Reason">Why; always required, and recorded with the variance.</param>
public sealed record PricingOverrideRequest(decimal Rate, string Reason);
