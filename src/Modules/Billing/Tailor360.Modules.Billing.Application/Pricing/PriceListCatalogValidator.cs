using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Modules.Catalog.Contracts.Catalogue;

namespace Tailor360.Modules.Billing.Application.Pricing;

/// <summary>
/// Link 4 of <c>docs/prd/category-hierarchy.md</c> section 5: a service type's or a design option's
/// <c>price_list_item_code</c> must name an active item of the published price-list version that
/// prices each branch the catalogue offers it at. A branch no version prices yet is a warning, so a
/// catalogue can be published ahead of its prices (decision OD-19 in
/// <c>docs/prd/assumptions-and-open-decisions.md</c>); an item missing or retired where a version does
/// price the branch is an error.
/// </summary>
/// <remarks>
/// Registered as an <see cref="ICatalogDependencyValidator"/>, because the Catalog module deliberately
/// knows nothing about what is on the other end of the link: reading Billing's tables to check it
/// would be the boundary violation the architecture exists to prevent. This is Billing answering
/// about its own configuration.
/// </remarks>
/// <param name="store">The price-list store.</param>
public sealed class PriceListCatalogValidator(IPriceListStore store) : ICatalogDependencyValidator
{
    /// <summary>The name findings are attributed to.</summary>
    public const string ValidatorName = "price-lists";

    /// <inheritdoc />
    public string Name => ValidatorName;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CatalogFinding>> ValidatePublicationAsync(
        CatalogPublicationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var linkedServices = candidate.ServiceTypes.Where(service => service.PriceListItemCode is not null).ToArray();
        var linkedOptions = candidate.DesignGroups
            .SelectMany(group => group.Options
                .Where(option => option.PriceListItemCode is not null)
                .Select(option => (Group: group, Option: option)))
            .ToArray();
        if (linkedServices.Length == 0 && linkedOptions.Length == 0)
        {
            return [];
        }

        var published = await store.PublishedVersionsAsync(candidate.OrganisationId, cancellationToken);
        var categories = candidate.Categories.ToDictionary(category => category.Id);
        var findings = new List<CatalogFinding>();

        foreach (var service in linkedServices)
        {
            var category = categories.GetValueOrDefault(service.CategoryId);
            var reference = $"{category?.Code ?? "?"}.{service.Code}";
            var branches = category is null ? service.BranchIds : service.BranchIds.Intersect(category.BranchIds).ToArray();
            Check(published, service.PriceListItemCode!, branches, reference, $"serviceTypes[{reference}].priceListItemCode", findings);
        }

        foreach (var (group, option) in linkedOptions)
        {
            var category = categories.GetValueOrDefault(group.CategoryId);
            var reference = $"{category?.Code ?? "?"}.{group.Code}.{option.Code}";
            var branches = category is null ? group.BranchIds : group.BranchIds.Intersect(category.BranchIds).ToArray();
            Check(published, option.PriceListItemCode!, branches, reference, $"designOptions[{reference}].priceListItemCode", findings);
        }

        return findings;
    }

    private static void Check(
        IReadOnlyList<PriceListVersion> published,
        string itemCode,
        IEnumerable<Guid> branches,
        string reference,
        string target,
        List<CatalogFinding> findings)
    {
        var unpriced = 0;
        var missing = 0;
        var retired = 0;
        foreach (var branch in branches.Distinct())
        {
            var version = published.FirstOrDefault(version => version.Covers(branch));
            if (version is null)
            {
                unpriced++;
                continue;
            }

            var item = version.FindItemByCode(itemCode);
            if (item is null)
            {
                missing++;
            }
            else if (!item.Active)
            {
                retired++;
            }
        }

        if (unpriced > 0)
        {
            // A warning, not an error (OD-19): a catalogue is published before its prices exist — the seed
            // and a new branch both start that way — and nothing can be ordered on it until a price-list
            // version prices the branch, because the pricing service refuses with no version in force.
            // Where a version does price the branch, the item it names must be there: that is the error,
            // and the price list's own publication check refuses to drop such an item from the other side.
            findings.Add(new CatalogFinding(
                CatalogFindingSeverity.Warning,
                "catalog.price-list-not-published",
                $"'{reference}' is offered at {unpriced} branch(es) that no published price-list version prices "
                + "yet, so it cannot be ordered there until one is published.",
                target));
        }

        if (missing > 0)
        {
            findings.Add(new CatalogFinding(
                CatalogFindingSeverity.Error,
                "catalog.price-list-item-unknown",
                $"'{reference}' names price-list item '{itemCode}', which the published price-list version pricing "
                + $"{missing} of its branch(es) does not hold.",
                target));
        }

        if (retired > 0)
        {
            findings.Add(new CatalogFinding(
                CatalogFindingSeverity.Error,
                "catalog.price-list-item-retired",
                $"'{reference}' names price-list item '{itemCode}', which is retired in the published price-list "
                + $"version pricing {retired} of its branch(es).",
                target));
        }
    }
}
