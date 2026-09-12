using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Modules.Billing.Domain.Tax;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>Start a tax configuration draft, empty or as a copy.</summary>
public sealed record CreateTaxConfigurationDraftRequest(
    string? Name,
    string? Notes,
    DateOnly? EffectiveFrom,
    Guid? CloneFromVersionId);

/// <summary>Change a draft's own details.</summary>
public sealed record DescribeTaxConfigurationRequest(
    string? Name,
    string? Notes,
    DateOnly? EffectiveFrom,
    string? Reason);

/// <summary>One component's rate, as sent.</summary>
public sealed record TaxRateRequest(string? Kind, decimal? RatePercent)
{
    /// <summary>
    /// The rate as the domain reads it. An unknown kind and an omitted rate each become a value the
    /// domain refuses, so neither is silently read as something else.
    /// </summary>
    public TaxRate ToRate()
        => new(
            Enum.TryParse<TaxComponentKind>(Kind, ignoreCase: false, out var kind) ? kind : (TaxComponentKind)(-1),
            RatePercent ?? -1m);
}

/// <summary>Add or replace a tax code.</summary>
public sealed record TaxCodeRequest(
    string? Code,
    string? Description,
    string? Classification,
    string? Kind,
    bool Active,
    IReadOnlyList<TaxRateRequest>? Rates,
    string? Reason)
{
    /// <summary>The details as the domain reads them.</summary>
    public TaxCodeDetails ToDetails()
        => new(
            Code ?? string.Empty,
            Description ?? string.Empty,
            Classification ?? string.Empty,
            Enum.TryParse<TaxCodeKind>(Kind, ignoreCase: false, out var kind) ? kind : (TaxCodeKind)(-1),
            Active,
            [.. (Rates ?? []).Select(rate => rate.ToRate())]);
}

/// <summary>A reason alone.</summary>
public sealed record BillingReasonRequest(string? Reason);

/// <summary>Record or amend a GST registration.</summary>
public sealed record GstRegistrationRequest(
    Guid BranchId,
    string? Gstin,
    string? StateCode,
    string? LegalName,
    string? TradeName,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Reason)
{
    /// <summary>The details as the domain reads them; an omitted first day is one the domain refuses.</summary>
    public GstRegistrationDetails ToDetails()
        => new(
            BranchId,
            (Gstin ?? string.Empty).Trim().ToUpperInvariant(),
            (StateCode ?? string.Empty).Trim(),
            LegalName ?? string.Empty,
            TradeName,
            EffectiveFrom ?? default,
            EffectiveTo);
}
