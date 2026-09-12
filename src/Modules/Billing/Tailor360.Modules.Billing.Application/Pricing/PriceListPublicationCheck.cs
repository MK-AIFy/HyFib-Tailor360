using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Modules.Catalog.Contracts.Catalogue;

namespace Tailor360.Modules.Billing.Application.Pricing;

/// <summary>
/// What a price-list version must satisfy before it may be published.
/// </summary>
/// <remarks>
/// Pure and static, over the version, what its list published before, the published tax
/// configuration, the other lists' published versions and what the published catalogue names. It
/// encodes no rate: the checks say only what would make a calculation impossible — an item whose tax
/// code nobody published, a branch two versions both claim to price, a service the counter offers
/// whose item the version dropped — or a document unreadable across years.
/// </remarks>
public static class PriceListPublicationCheck
{
    /// <summary>Checks a version.</summary>
    /// <param name="version">The version.</param>
    /// <param name="ledger">What the list's published versions spelled their codes as.</param>
    /// <param name="published">The list's currently published version, or null.</param>
    /// <param name="taxConfiguration">The published tax configuration, or null when none is.</param>
    /// <param name="otherPublished">Every other list's published version.</param>
    /// <param name="catalogueReferences">Every item code the published catalogue names, and where.</param>
    /// <returns>The report.</returns>
    public static BillingValidationReport Run(
        PriceListVersion version,
        PriceListLedger ledger,
        PriceListVersion? published,
        TaxConfigurationVersion? taxConfiguration,
        IReadOnlyList<PriceListVersion> otherPublished,
        IReadOnlyList<CatalogPriceListItemReference> catalogueReferences)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(otherPublished);
        ArgumentNullException.ThrowIfNull(catalogueReferences);

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

        if (!version.BranchIds.Any())
        {
            findings.Add(BillingFinding.Error(
                "billing.no-branches",
                "The version prices for no branch, so nothing could ever be priced on it.",
                "branchIds"));
        }

        foreach (var other in otherPublished.Where(other => other.PriceListId != version.PriceListId))
        {
            var shared = version.BranchIds.Intersect(other.BranchIds).ToArray();
            if (shared.Length > 0)
            {
                findings.Add(BillingFinding.Error(
                    "billing.branch-priced-twice",
                    $"{shared.Length} of the version's branch(es) are already priced by another list's published "
                    + "version. A branch is priced by exactly one published version at a time; end that one's "
                    + "coverage first.",
                    "branchIds"));
            }
        }

        CheckCatalogue(version, published, otherPublished, catalogueReferences, findings);

        if (version.Items.Count == 0)
        {
            findings.Add(BillingFinding.Warning(
                "billing.no-items",
                "The version holds no item, so no service could be priced on it.",
                "items"));
        }

        if (taxConfiguration is null && version.Items.Count > 0)
        {
            findings.Add(BillingFinding.Error(
                "billing.tax-configuration-missing",
                "No tax configuration version is published, so no item's tax code can be resolved. Publish one first.",
                "items"));
        }

        foreach (var item in version.Items.OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            if (taxConfiguration is not null)
            {
                var taxCode = taxConfiguration.FindTaxCodeByCode(item.TaxCode);
                if (taxCode is null)
                {
                    findings.Add(BillingFinding.Error(
                        "billing.tax-code-unknown",
                        $"'{item.Code}' names tax code '{item.TaxCode}', which the published tax configuration does "
                        + "not hold.",
                        ItemTarget(item, "taxCode")));
                }
                else if (!taxCode.Active && item.Active)
                {
                    findings.Add(BillingFinding.Error(
                        "billing.tax-code-retired",
                        $"'{item.Code}' names tax code '{item.TaxCode}', which is retired. An active item needs a code "
                        + "that can still be charged.",
                        ItemTarget(item, "taxCode")));
                }
            }

            if (ledger.ItemCodeByKey.TryGetValue(item.Key, out var publishedAs)
                && !string.Equals(publishedAs, item.Code, StringComparison.Ordinal))
            {
                findings.Add(BillingFinding.Error(
                    "billing.published-code-changed",
                    $"'{item.Code}' was published as '{publishedAs}', and a published code never changes: the "
                    + "catalogue and confirmed orders refer to it. Add a new item and retire this one instead.",
                    ItemTarget(item, "code")));
            }

            if (ledger.ItemKeyByCode.TryGetValue(item.Code, out var owner) && owner != item.Key)
            {
                findings.Add(BillingFinding.Error(
                    "billing.code-reused",
                    $"'{item.Code}' was published for another item, and a code is never re-used for a different "
                    + "thing: a report over two years would add unlike things.",
                    ItemTarget(item, "code")));
            }
        }

        foreach (var rule in version.DiscountRules.OrderBy(rule => rule.Code, StringComparer.Ordinal))
        {
            if (ledger.RuleCodeByKey.TryGetValue(rule.Key, out var publishedAs)
                && !string.Equals(publishedAs, rule.Code, StringComparison.Ordinal))
            {
                findings.Add(BillingFinding.Error(
                    "billing.published-code-changed",
                    $"Discount rule '{rule.Code}' was published as '{publishedAs}', and a published code never changes.",
                    $"discountRules[{rule.Code}].code"));
            }

            if (ledger.RuleKeyByCode.TryGetValue(rule.Code, out var owner) && owner != rule.Key)
            {
                findings.Add(BillingFinding.Error(
                    "billing.code-reused",
                    $"Discount rule '{rule.Code}' was published for another rule, and a code is never re-used.",
                    $"discountRules[{rule.Code}].code"));
            }
        }

        return new BillingValidationReport(
            version.Id,
            [.. findings.OrderByDescending(finding => finding.Severity).ThenBy(finding => finding.Code, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Publishing retires the outgoing version, so the draft must still price everything the published
    /// catalogue offers at the branches it takes over: an item the catalogue names must be active in the
    /// draft where the draft prices the branch, and a branch the outgoing version priced may not be left
    /// with no version at all while the catalogue still offers a priced service there. The catalogue's own
    /// publish validation guards the same link from the other side; this is the half that stops a price
    /// list stranding a catalogue that was validated against its predecessor.
    /// </summary>
    private static void CheckCatalogue(
        PriceListVersion version,
        PriceListVersion? published,
        IReadOnlyList<PriceListVersion> otherPublished,
        IReadOnlyList<CatalogPriceListItemReference> catalogueReferences,
        List<BillingFinding> findings)
    {
        foreach (var reference in catalogueReferences.OrderBy(reference => reference.Reference, StringComparer.Ordinal))
        {
            if (!reference.BranchIds.Any(version.Covers))
            {
                continue;
            }

            var item = version.FindItemByCode(reference.ItemCode);
            if (item is null || !item.Active)
            {
                findings.Add(BillingFinding.Error(
                    "billing.catalogue-reference-lost",
                    $"The published catalogue offers '{reference.Reference}' against item '{reference.ItemCode}' at a "
                    + $"branch this version prices, and the version {(item is null ? "does not hold" : "retires")} that "
                    + "item. Publishing it would leave the service orderable with nothing to price it by.",
                    $"items[{reference.ItemCode}]"));
            }
        }

        if (published is null || published.Id == version.Id)
        {
            return;
        }

        var stillPricedElsewhere = otherPublished
            .Where(other => other.PriceListId != version.PriceListId)
            .SelectMany(other => other.BranchIds)
            .ToHashSet();
        var leftUnpriced = published.BranchIds
            .Where(branch => !version.Covers(branch) && !stillPricedElsewhere.Contains(branch))
            .Where(branch => catalogueReferences.Any(reference => reference.BranchIds.Contains(branch)))
            .Count();
        if (leftUnpriced > 0)
        {
            findings.Add(BillingFinding.Error(
                "billing.branch-left-unpriced",
                $"{leftUnpriced} branch(es) the published version prices are dropped by this one, and the published "
                + "catalogue offers priced services there. Keep the branch, or publish another list's version for it "
                + "first.",
                "branchIds"));
        }
    }

    private static string ItemTarget(PriceListItem item, string field) => $"items[{item.Code}].{field}";
}
