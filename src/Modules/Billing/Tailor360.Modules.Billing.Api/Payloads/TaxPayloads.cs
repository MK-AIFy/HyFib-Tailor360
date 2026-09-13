using Tailor360.Modules.Billing.Application;
using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Modules.Billing.Domain.Tax;

namespace Tailor360.Modules.Billing.Api.Payloads;

/// <summary>A tax configuration version without its codes: what a list shows.</summary>
public sealed record TaxConfigurationSummaryPayload(
    Guid TaxConfigurationVersionId,
    int VersionNumber,
    string Name,
    string? Notes,
    string Status,
    DateOnly EffectiveFrom,
    Guid? ClonedFromVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? RetiredAt)
{
    /// <summary>Projects a version.</summary>
    public static TaxConfigurationSummaryPayload From(TaxConfigurationVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new TaxConfigurationSummaryPayload(
            version.Id,
            version.VersionNumber,
            version.Name,
            version.Notes,
            version.Status.ToString(),
            version.EffectiveFrom,
            version.ClonedFromVersionId,
            version.CreatedAt,
            version.PublishedAt,
            version.RetiredAt);
    }
}

/// <summary>A tax configuration version and its codes.</summary>
public sealed record TaxConfigurationPayload(
    TaxConfigurationSummaryPayload Version,
    IReadOnlyList<TaxCodePayload> TaxCodes)
{
    /// <summary>Projects a version and everything in it.</summary>
    public static TaxConfigurationPayload From(TaxConfigurationVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new TaxConfigurationPayload(
            TaxConfigurationSummaryPayload.From(version),
            [.. version.TaxCodes.OrderBy(code => code.Code, StringComparer.Ordinal).Select(TaxCodePayload.From)]);
    }
}

/// <summary>One component's rate.</summary>
public sealed record TaxRatePayload(string Kind, decimal RatePercent)
{
    /// <summary>Projects a rate.</summary>
    public static TaxRatePayload From(TaxRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);

        return new TaxRatePayload(rate.Kind.ToString(), rate.RatePercent);
    }
}

/// <summary>One tax code.</summary>
public sealed record TaxCodePayload(
    Guid TaxCodeId,
    Guid TaxCodeKey,
    string Code,
    string Description,
    string Classification,
    string Kind,
    bool Active,
    IReadOnlyList<TaxRatePayload> Rates)
{
    /// <summary>Projects a code.</summary>
    public static TaxCodePayload From(TaxCode code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return new TaxCodePayload(
            code.Id,
            code.Key,
            code.Code,
            code.Description,
            code.Classification,
            code.Kind.ToString(),
            code.Active,
            [.. code.Rates.Select(TaxRatePayload.From)]);
    }
}

/// <summary>One thing a publication check found.</summary>
public sealed record BillingFindingPayload(string Severity, string Code, string Message, string? Target)
{
    /// <summary>Projects a finding.</summary>
    public static BillingFindingPayload From(BillingFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return new BillingFindingPayload(finding.Severity.ToString(), finding.Code, finding.Message, finding.Target);
    }
}

/// <summary>What the publication checks found.</summary>
public sealed record BillingValidationReportPayload(
    Guid VersionId,
    bool CanPublish,
    IReadOnlyList<BillingFindingPayload> Findings)
{
    /// <summary>Projects a report.</summary>
    public static BillingValidationReportPayload From(BillingValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new BillingValidationReportPayload(
            report.VersionId, !report.HasErrors, [.. report.Findings.Select(BillingFindingPayload.From)]);
    }
}

/// <summary>What a publication produced.</summary>
public sealed record TaxConfigurationPublicationPayload(
    TaxConfigurationPayload Published,
    Guid? SupersededVersionId,
    IReadOnlyList<BillingFindingPayload> Findings);

/// <summary>A branch's GST registration.</summary>
public sealed record GstRegistrationPayload(
    Guid GstRegistrationId,
    Guid BranchId,
    string Gstin,
    string StateCode,
    string LegalName,
    string? TradeName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a registration.</summary>
    public static GstRegistrationPayload From(GstRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return new GstRegistrationPayload(
            registration.Id,
            registration.BranchId,
            registration.Gstin,
            registration.StateCode,
            registration.LegalName,
            registration.TradeName,
            registration.EffectiveFrom,
            registration.EffectiveTo,
            registration.CreatedAt,
            registration.UpdatedAt);
    }
}
