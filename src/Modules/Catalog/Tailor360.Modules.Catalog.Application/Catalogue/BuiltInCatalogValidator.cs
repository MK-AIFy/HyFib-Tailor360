using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// The checks the catalogue makes about itself, before anybody else's validator is asked.
/// </summary>
/// <remarks>
/// <para>
/// Registered exactly like the validators other modules contribute, and deliberately so: the built-in
/// rules are not privileged, they simply happen to be about the hierarchy rather than about a
/// reference into another module. Section 10 of <c>docs/prd/category-hierarchy.md</c> is the list, and
/// each row there is one method below.
/// </para>
/// <para>
/// <strong>Every check runs.</strong> None of them stops at the first failure, because an
/// administrator fixing one code at a time and re-submitting is a far worse afternoon than a single
/// report naming everything wrong at once.
/// </para>
/// </remarks>
public sealed class BuiltInCatalogValidator : ICatalogDependencyValidator
{
    /// <summary>The name findings from this validator are attributed to.</summary>
    public const string ValidatorName = "catalog";

    /// <inheritdoc />
    public string Name => ValidatorName;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<CatalogFinding>> ValidatePublicationAsync(
        CatalogPublicationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var findings = new List<CatalogFinding>();

        CheckCategoryCodes(candidate, findings);
        CheckServiceCodes(candidate, findings);
        CheckHierarchy(candidate, findings);
        CheckBranchAvailability(candidate, findings);
        CheckLinks(candidate, findings);
        CheckGroupingNodes(candidate, findings);

        return ValueTask.FromResult<IReadOnlyList<CatalogFinding>>(findings);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<CatalogFinding>> ValidateRetirementAsync(
        CatalogRetirementCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // A warning rather than an error, because retiring the last published version is a decision an
        // Owner is allowed to take — closing for a refit, or retiring before the replacement is ready.
        // What it must not be is a surprise, and the shop discovering it at the counter is exactly the
        // surprise this sentence prevents. Whether work is stranded is a question only the modules
        // holding that work can answer, and they answer it through their own validators.
        IReadOnlyList<CatalogFinding> findings = candidate.SuccessorVersionId is null
            ?
            [
                CatalogFinding.Warning(
                    "catalog.retirement-leaves-nothing-published",
                    "No other catalogue version is published, so once this one is retired the branches "
                    + "will not be able to take any new order until a version is published."),
            ]
            : [];

        return ValueTask.FromResult(findings);
    }

    private static void CheckCategoryCodes(
        CatalogPublicationCandidate candidate,
        List<CatalogFinding> findings)
    {
        foreach (var duplicate in candidate.Categories
                     .GroupBy(category => category.Code, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            findings.Add(CatalogFinding.Error(
                "catalog.duplicate-category-code",
                $"{duplicate.Count()} categories share the code '{duplicate.Key}'. A code identifies "
                + "one category, and it is what price lists, reports and exports refer to.",
                CategoryTarget(duplicate.Key, "code")));
        }

        foreach (var category in candidate.Categories)
        {
            CheckAgainstHistory(
                candidate.CategoryCodeHistory,
                category.Key,
                category.Code,
                CategoryTarget(category.Code, "code"),
                "category",
                findings);
        }
    }

    private static void CheckServiceCodes(
        CatalogPublicationCandidate candidate,
        List<CatalogFinding> findings)
    {
        var categoryCodes = candidate.Categories.ToDictionary(
            category => category.Id, category => category.Code);

        foreach (var group in candidate.ServiceTypes.GroupBy(service => service.CategoryId))
        {
            if (!categoryCodes.TryGetValue(group.Key, out var categoryCode))
            {
                // Its category is missing entirely, which CheckHierarchy reports. Saying it twice from
                // two angles would bury the finding that names the actual cause.
                continue;
            }

            foreach (var duplicate in group
                         .GroupBy(service => service.Code, StringComparer.Ordinal)
                         .Where(codes => codes.Count() > 1))
            {
                findings.Add(CatalogFinding.Error(
                    "catalog.duplicate-service-code",
                    $"{duplicate.Count()} service types of '{categoryCode}' share the code "
                    + $"'{duplicate.Key}'. A service code is unique within its category.",
                    ServiceTarget(categoryCode, duplicate.Key, "code")));
            }
        }

        foreach (var service in candidate.ServiceTypes)
        {
            if (!categoryCodes.TryGetValue(service.CategoryId, out var categoryCode))
            {
                continue;
            }

            CheckAgainstHistory(
                candidate.ServiceTypeCodeHistory,
                service.Key,
                Qualified(categoryCode, service.Code),
                ServiceTarget(categoryCode, service.Code, "code"),
                "service type",
                findings);
        }
    }

    /// <summary>
    /// The two rules that are about the past rather than about the draft.
    /// </summary>
    /// <remarks>
    /// A code is immutable once the version that introduced it is published, and it is never re-used
    /// for a different concept after retirement (<c>docs/prd/category-hierarchy.md</c> section 4).
    /// Both exist for the same reason: a price list, a report and an export all refer to the code, and
    /// a code that quietly changed meaning turns a correct historical figure into a wrong one with
    /// nothing in the data to show it happened.
    /// </remarks>
    private static void CheckAgainstHistory(
        CatalogCodeHistory history,
        Guid key,
        string code,
        string target,
        string noun,
        List<CatalogFinding> findings)
    {
        if (history.CodeByKey.TryGetValue(key, out var published)
            && !string.Equals(published, code, StringComparison.Ordinal))
        {
            findings.Add(CatalogFinding.Error(
                "catalog.published-code-changed",
                $"This {noun} was published as '{published}' and this draft calls it '{code}'. A code "
                + "is fixed once it has been published: correct the label instead, which changes what "
                + "is shown and breaks nothing.",
                target));
        }

        if (history.KeyByCode.TryGetValue(code, out var owner) && owner != key)
        {
            findings.Add(CatalogFinding.Error(
                "catalog.published-code-reused",
                $"The code '{code}' has already been published for a different {noun}. A code is never "
                + "re-used for a second thing, even after the first is retired, because every price "
                + "list, report and export that mentions it would silently change meaning.",
                target));
        }
    }

    private static void CheckHierarchy(
        CatalogPublicationCandidate candidate,
        List<CatalogFinding> findings)
    {
        var byId = candidate.Categories.ToDictionary(category => category.Id);

        foreach (var category in candidate.Categories)
        {
            if (category.ParentId is { } parentId && !byId.ContainsKey(parentId))
            {
                findings.Add(CatalogFinding.Error(
                    "catalog.orphaned-parent",
                    $"'{category.Code}' names a parent category that is not in this version.",
                    CategoryTarget(category.Code, "parentCategoryId")));
            }
        }

        foreach (var category in candidate.Categories.Where(category => IsInCycle(category, byId)))
        {
            findings.Add(CatalogFinding.Error(
                "catalog.hierarchy-cycle",
                $"'{category.Code}' is its own ancestor, which makes the hierarchy circular and every "
                + "walk of it endless.",
                CategoryTarget(category.Code, "parentCategoryId")));
        }

        foreach (var service in candidate.ServiceTypes.Where(
                     service => !byId.ContainsKey(service.CategoryId)))
        {
            findings.Add(CatalogFinding.Error(
                "catalog.orphaned-service-type",
                $"The service type '{service.Code}' names a category that is not in this version.",
                $"serviceTypes[{service.Code}].categoryId"));
        }
    }

    private static bool IsInCycle(
        CatalogCategoryView category,
        Dictionary<Guid, CatalogCategoryView> byId)
    {
        var seen = new HashSet<Guid> { category.Id };
        var current = category;

        while (current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
        {
            if (!seen.Add(parent.Id))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static void CheckBranchAvailability(
        CatalogPublicationCandidate candidate,
        List<CatalogFinding> findings)
    {
        var byId = candidate.Categories.ToDictionary(category => category.Id);

        foreach (var category in candidate.Categories)
        {
            if (category.ParentId is { } parentId
                && byId.TryGetValue(parentId, out var parent)
                && category.BranchIds.Except(parent.BranchIds).ToArray() is { Length: > 0 } extra)
            {
                findings.Add(CatalogFinding.Error(
                    "catalog.branches-not-subset-of-parent",
                    $"'{category.Code}' is offered at {extra.Length} branch(es) where its parent "
                    + $"'{parent.Code}' is not. A sub-category cannot be offered where the category it "
                    + "belongs to is not.",
                    CategoryTarget(category.Code, "branchIds")));
            }

            if (category.BranchIds.Count == 0)
            {
                findings.Add(CatalogFinding.Warning(
                    "catalog.category-offered-nowhere",
                    $"'{category.Code}' is not offered at any branch, so nothing under it can be "
                    + "ordered. An empty set of branches means nowhere, not everywhere.",
                    CategoryTarget(category.Code, "branchIds")));
            }
        }

        foreach (var service in candidate.ServiceTypes)
        {
            if (!byId.TryGetValue(service.CategoryId, out var category))
            {
                continue;
            }

            if (service.BranchIds.Except(category.BranchIds).ToArray() is { Length: > 0 } extra)
            {
                findings.Add(CatalogFinding.Error(
                    "catalog.branches-not-subset-of-category",
                    $"'{Qualified(category.Code, service.Code)}' is offered at {extra.Length} "
                    + "branch(es) where its category is not.",
                    ServiceTarget(category.Code, service.Code, "branchIds")));
            }
        }
    }

    private static void CheckLinks(
        CatalogPublicationCandidate candidate,
        List<CatalogFinding> findings)
    {
        var categoryCodes = candidate.Categories.ToDictionary(
            category => category.Id, category => category.Code);

        foreach (var service in candidate.ServiceTypes)
        {
            var missing = MissingLinks(service);

            if (missing.Count == 0)
            {
                continue;
            }

            var categoryCode = categoryCodes.GetValueOrDefault(service.CategoryId, "?");
            var reference = Qualified(categoryCode, service.Code);
            var named = string.Join(", ", missing);

            findings.Add(service.AllowIncomplete
                ? CatalogFinding.Warning(
                    "catalog.service-type-incomplete",
                    $"'{reference}' has no {named}, so it will be published but flagged not orderable: "
                    + "it will not be listed at intake and will not be accepted on an order.",
                    ServiceTarget(categoryCode, service.Code, missing[0]))
                : CatalogFinding.Error(
                    "catalog.service-type-missing-link",
                    $"'{reference}' has no {named}. Supply the missing configuration, or accept "
                    + "publication without it, which flags the service not orderable.",
                    ServiceTarget(categoryCode, service.Code, missing[0])));
        }
    }

    private static void CheckGroupingNodes(
        CatalogPublicationCandidate candidate,
        List<CatalogFinding> findings)
    {
        var grouping = candidate.Categories
            .Where(category => category.ParentId is not null)
            .Select(category => category.ParentId!.Value)
            .ToHashSet();

        var byId = candidate.Categories.ToDictionary(category => category.Id);

        foreach (var service in candidate.ServiceTypes.Where(
                     service => grouping.Contains(service.CategoryId)))
        {
            var categoryCode = byId[service.CategoryId].Code;

            // Rule 1 of section 2: a category with sub-categories is a grouping node, orders are placed
            // against its sub-categories, and IsOrderable is false for it. A service type here would
            // therefore be configuration nobody could ever order — and worse, it would show in an
            // administrator's tree as though it could be. Moving it down to the sub-categories is what
            // was meant, and refusing publication is what prompts it.
            findings.Add(CatalogFinding.Error(
                "catalog.service-type-on-grouping-node",
                $"'{categoryCode}' has sub-categories, so it is a grouping node and nothing is ordered "
                + $"against it directly. The service type '{service.Code}' on it could never be "
                + "ordered; it belongs on the sub-categories.",
                ServiceTarget(categoryCode, service.Code, "categoryId")));
        }

        foreach (var category in candidate.Categories.Where(
                     category => !grouping.Contains(category.Id)
                                 && candidate.ServiceTypes.All(service => service.CategoryId != category.Id)))
        {
            findings.Add(CatalogFinding.Warning(
                "catalog.category-has-no-services",
                $"'{category.Code}' has neither sub-categories nor service types, so there is nothing "
                + "to order against it.",
                CategoryTarget(category.Code, "serviceTypes")));
        }

        foreach (var category in candidate.Categories)
        {
            if (category.ParentId is { } parentId
                && byId.TryGetValue(parentId, out var parent)
                && !CatalogCode.FollowsParentPrefix(category.Code, parent.Code))
            {
                findings.Add(CatalogFinding.Warning(
                    "catalog.subcategory-code-not-prefixed",
                    $"'{category.Code}' does not begin with '{parent.Code}_'. The convention makes "
                    + "lineage readable without a lookup; it is a warning because a code inherited "
                    + "from a shop's existing paperwork is still valid.",
                    CategoryTarget(category.Code, "code")));
            }
        }
    }

    private static List<string> MissingLinks(CatalogServiceTypeView service)
    {
        var missing = new List<string>(4);

        if (service.MeasurementTemplateId is null)
        {
            missing.Add("measurementTemplateId");
        }

        if (service.WorkflowDefinitionId is null)
        {
            missing.Add("workflowDefinitionId");
        }

        if (string.IsNullOrWhiteSpace(service.PriceListItemCode))
        {
            missing.Add("priceListItemCode");
        }

        if (service.QcChecklistTemplateId is null)
        {
            missing.Add("qcChecklistTemplateId");
        }

        return missing;
    }

    private static string Qualified(string categoryCode, string serviceCode)
        => $"{categoryCode}.{serviceCode}";

    private static string CategoryTarget(string code, string field) => $"categories[{code}].{field}";

    private static string ServiceTarget(string categoryCode, string serviceCode, string field)
        => $"serviceTypes[{Qualified(categoryCode, serviceCode)}].{field}";
}
