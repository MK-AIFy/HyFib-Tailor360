using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Narrows a category-wide <see cref="DesignEvaluation"/> down to what one service type actually offers
/// (#140).
/// </summary>
/// <remarks>
/// <see cref="IDesignSelectionValidator"/> reads the whole category's groups and rules — the one code
/// path for the rules, never duplicated — but a service type only ever links a subset of its category's
/// groups (<see cref="ServiceType.DesignOptionGroupIds"/>). A required group, or a <c>requires</c> rule's
/// consequent, that belongs only to a sibling service is not this draft's business: it must never gate
/// this draft's confirmability, and an option it auto-selected must never reach this draft's snapshot.
/// This is the one place both <see cref="DesignSelectionDraftHandler"/> and
/// <see cref="DesignSelectionQuery"/> resolve that scope and apply it, rather than each narrowing the
/// evaluator's answer its own way.
/// </remarks>
internal static class DesignEvaluationScope
{
    /// <summary>The codes of the groups a service type actually offers, in the version it belongs to.</summary>
    /// <param name="version">The version the service type is a row of.</param>
    /// <param name="serviceType">The service type.</param>
    /// <returns>The offered groups' codes.</returns>
    public static IReadOnlySet<string> OfferedGroupCodesOf(CatalogVersion version, ServiceType serviceType)
        => DesignSelectionMigration.GroupsOf(version, serviceType)
            .Select(group => group.Code)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Keeps only what is about a group the service actually offers, and recomputes confirmability from
    /// what remains.
    /// </summary>
    /// <param name="evaluation">The category-wide evaluation <see cref="IDesignSelectionValidator"/> answered.</param>
    /// <param name="offeredGroupCodes">The codes of the groups the draft's own service type offers.</param>
    /// <returns>The narrowed evaluation.</returns>
    public static DesignEvaluation Narrow(DesignEvaluation evaluation, IReadOnlySet<string> offeredGroupCodes)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(offeredGroupCodes);

        var violations = evaluation.Violations
            .Where(violation => violation.GroupCode is null || offeredGroupCodes.Contains(violation.GroupCode))
            .ToList();

        var autoSelections = evaluation.AutoSelections
            .Where(auto => offeredGroupCodes.Contains(auto.GroupCode))
            .ToList();

        return new DesignEvaluation(
            violations.All(violation => !violation.Blocks),
            violations,
            autoSelections,
            evaluation.Notes);
    }
}
