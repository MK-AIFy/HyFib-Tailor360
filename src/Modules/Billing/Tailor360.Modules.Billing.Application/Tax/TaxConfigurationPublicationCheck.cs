using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain.Tax;

namespace Tailor360.Modules.Billing.Application.Tax;

/// <summary>
/// What a tax configuration version must satisfy before it may be published.
/// </summary>
/// <remarks>
/// <para>
/// The checks say only what would make a calculation contradict itself or a document unreadable;
/// they encode no rate. A CGST without an SGST would tax an intra-state supply by half; a CGST and
/// SGST that differ would print two "half" rates that are not halves; an IGST that is not their sum
/// would tax the same supply differently by geography. Those are arithmetic, not statute — the
/// accountant still decides every number.
/// </para>
/// <para>
/// Pure and static, so a unit test builds a version and reads the report; the handler passes the
/// ledger the store read.
/// </para>
/// </remarks>
public static class TaxConfigurationPublicationCheck
{
    /// <summary>Checks a version against itself and against what was published before it.</summary>
    /// <param name="version">The version.</param>
    /// <param name="ledger">What every published version spelled its codes as.</param>
    /// <param name="published">The version currently published, or null.</param>
    /// <returns>The report.</returns>
    public static BillingValidationReport Run(
        TaxConfigurationVersion version,
        TaxCodeLedger ledger,
        TaxConfigurationVersion? published)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(ledger);

        var findings = new List<BillingFinding>();

        if (published is not null && published.Id != version.Id && version.EffectiveFrom < published.EffectiveFrom)
        {
            findings.Add(BillingFinding.Error(
                "billing.effective-from-before-published",
                $"The version would apply from {version.EffectiveFrom:yyyy-MM-dd}, before the published version's "
                + $"{published.EffectiveFrom:yyyy-MM-dd}. A calculation already made on the published version "
                + "would then have been made on the wrong one.",
                "effectiveFrom"));
        }

        if (version.TaxCodes.Count == 0)
        {
            findings.Add(BillingFinding.Warning(
                "billing.no-tax-codes",
                "The version holds no tax code, so no price-list item could name one and nothing could be priced.",
                "taxCodes"));
        }

        foreach (var code in version.TaxCodes.OrderBy(code => code.Code, StringComparer.Ordinal))
        {
            CheckComponents(code, findings);
            CheckHistory(code, ledger, findings);
        }

        return new BillingValidationReport(
            version.Id,
            [.. findings.OrderByDescending(finding => finding.Severity).ThenBy(finding => finding.Code, StringComparer.Ordinal)]);
    }

    private static void CheckComponents(TaxCode code, List<BillingFinding> findings)
    {
        var cgst = code.Components.FirstOrDefault(component => component.Kind == TaxComponentKind.Cgst);
        var sgst = code.Components.FirstOrDefault(component => component.Kind == TaxComponentKind.Sgst);
        var igst = code.Components.FirstOrDefault(component => component.Kind == TaxComponentKind.Igst);

        if ((cgst is null) != (sgst is null))
        {
            findings.Add(BillingFinding.Error(
                "billing.intra-state-pair-incomplete",
                $"'{code.Code}' carries {(cgst is null ? "SGST without CGST" : "CGST without SGST")}. An intra-state "
                + "supply is taxed by both halves or by neither.",
                Target(code, "rates")));
        }
        else if (cgst is not null && sgst is not null && cgst.RatePercent != sgst.RatePercent)
        {
            findings.Add(BillingFinding.Error(
                "billing.intra-state-pair-unequal",
                $"'{code.Code}' carries CGST and SGST at different rates. The two halves of an intra-state supply "
                + "are equal.",
                Target(code, "rates")));
        }

        if ((cgst is not null || sgst is not null) && igst is null)
        {
            findings.Add(BillingFinding.Error(
                "billing.inter-state-rate-missing",
                $"'{code.Code}' carries CGST and SGST and no IGST, so an inter-state supply under it could not be "
                + "taxed at all.",
                Target(code, "rates")));
        }
        else if (cgst is not null && sgst is not null && igst is not null
                 && igst.RatePercent != cgst.RatePercent + sgst.RatePercent)
        {
            findings.Add(BillingFinding.Error(
                "billing.inter-state-rate-not-sum",
                $"'{code.Code}' carries an IGST that is not the sum of its CGST and SGST, so the same supply would "
                + "be taxed differently by geography.",
                Target(code, "rates")));
        }
        else if (igst is not null && cgst is null && sgst is null)
        {
            findings.Add(BillingFinding.Error(
                "billing.intra-state-pair-missing",
                $"'{code.Code}' carries IGST and no CGST or SGST, so an intra-state supply under it could not be "
                + "taxed at all.",
                Target(code, "rates")));
        }

        if (cgst is null && sgst is null && igst is null
            && code.Components.Any(component => component.Kind == TaxComponentKind.Cess))
        {
            findings.Add(BillingFinding.Warning(
                "billing.cess-only-code",
                $"'{code.Code}' carries a cess and no GST component. A cess is an addition to a rate; on its own it "
                + "is most likely a code entered halfway.",
                Target(code, "rates")));
        }

        if (code.Active && code.Components.Count == 0)
        {
            findings.Add(BillingFinding.Warning(
                "billing.nil-rated-code",
                $"'{code.Code}' carries no component, so it is nil-rated or exempt. Say so in its description.",
                Target(code, "rates")));
        }
    }

    private static void CheckHistory(TaxCode code, TaxCodeLedger ledger, List<BillingFinding> findings)
    {
        if (ledger.CodeByKey.TryGetValue(code.Key, out var publishedAs)
            && !string.Equals(publishedAs, code.Code, StringComparison.Ordinal))
        {
            findings.Add(BillingFinding.Error(
                "billing.published-code-changed",
                $"'{code.Code}' was published as '{publishedAs}', and a published code never changes: invoices "
                + "already carry it. Add a new code and retire this one instead.",
                Target(code, "code")));
        }

        if (ledger.KeyByCode.TryGetValue(code.Code, out var owner) && owner != code.Key)
        {
            findings.Add(BillingFinding.Error(
                "billing.code-reused",
                $"'{code.Code}' was published for another tax code, and a code is never re-used for a different "
                + "classification: a report over two years would add unlike things.",
                Target(code, "code")));
        }
    }

    private static string Target(TaxCode code, string field) => $"taxCodes[{code.Code}].{field}";
}
