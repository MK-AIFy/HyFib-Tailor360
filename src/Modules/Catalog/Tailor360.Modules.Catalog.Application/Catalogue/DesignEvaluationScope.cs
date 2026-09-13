using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Narrows a category-wide <see cref="DesignEvaluation"/> down to what one service type actually offers
/// (#140).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IDesignSelectionValidator"/> reads the whole category's groups and rules — the one code
/// path for the rules, never duplicated — but a service type only ever links a subset of its category's
/// groups (<see cref="ServiceType.DesignOptionGroupIds"/>). A required group, or a <c>requires</c> rule's
/// consequent, that belongs only to a sibling service is not this draft's business: it must never gate
/// this draft's confirmability, and an option it auto-selected must never reach this draft's snapshot.
/// This is the one place both <see cref="DesignSelectionDraftHandler"/> and
/// <see cref="DesignSelectionQuery"/> resolve that scope and apply it, rather than each narrowing the
/// evaluator's answer its own way.
/// </para>
/// <para>
/// <strong>A group being offered is not enough on its own.</strong> A <c>requires</c> chain can settle a
/// sibling-service-only group first and then, from that, auto-select an option in a group this service
/// does offer — the second rule fires on the first rule's say-so, not on anything this draft's own picker
/// ever showed. Dropping only what directly names an unoffered group misses that: what actually decides
/// whether an auto-selection belongs to this draft is whether the value it followed from is itself
/// reachable from what the draft explicitly holds, offered group by offered group, all the way back —
/// so this walks the chain to a fixed point rather than checking one hop (Codex review, PR #221).
/// </para>
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
    /// Keeps only what is reachable from this draft's own service: a violation or an auto-selection whose
    /// groups the service offers, and — for an auto-selection specifically — whose triggering rule's
    /// antecedent is itself either explicitly held by the draft or reached the same way. Confirmability is
    /// recomputed from what remains.
    /// </summary>
    /// <param name="evaluation">The category-wide evaluation <see cref="IDesignSelectionValidator"/> answered.</param>
    /// <param name="version">The version the evaluation was read against, for the rules' own antecedents.</param>
    /// <param name="categoryId">The category the rules belong to.</param>
    /// <param name="serviceType">The draft's own service type.</param>
    /// <param name="explicitInputs">What the draft itself holds — always in scope, `Save` already refuses anything else.</param>
    /// <returns>The narrowed evaluation.</returns>
    public static DesignEvaluation Narrow(
        DesignEvaluation evaluation,
        CatalogVersion version,
        Guid categoryId,
        ServiceType serviceType,
        IReadOnlyCollection<DesignSelectionInput> explicitInputs)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(explicitInputs);

        var offeredGroupCodes = OfferedGroupCodesOf(version, serviceType);
        var rulesByIdentifier = version.DesignRulesOf(categoryId)
            .ToDictionary(rule => rule.Identifier, StringComparer.Ordinal);

        // Seeded with what the draft explicitly holds: always in scope, since Save refuses a value for a
        // group the service does not link. An auto-selection joins this set only once the rule that
        // produced it is shown to fire on something already in it — the fixed point a chain of rules
        // settles to, the same way the engine itself settles requires rules to a fixed point.
        var reachable = new HashSet<string>(
            explicitInputs.Select(input => input.GroupCode), StringComparer.Ordinal);

        var candidates = evaluation.AutoSelections
            .Where(auto => offeredGroupCodes.Contains(auto.GroupCode))
            .ToList();
        var autoSelections = new List<DesignAutoSelection>();

        bool settledOneMore;
        do
        {
            settledOneMore = false;
            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                var auto = candidates[i];
                var antecedentGroupCode = rulesByIdentifier.TryGetValue(auto.RuleIdentifier, out var rule)
                    ? rule.AntecedentGroupCode
                    : null;

                if (antecedentGroupCode is not null && !reachable.Contains(antecedentGroupCode))
                {
                    continue;
                }

                autoSelections.Add(auto);
                reachable.Add(auto.GroupCode);
                candidates.RemoveAt(i);
                settledOneMore = true;
            }
        }
        while (settledOneMore && candidates.Count > 0);

        // What never settled followed, transitively, from a group only a sibling service offers — dropped
        // the same as anything that named one directly.
        var violations = evaluation.Violations
            .Where(violation =>
                (violation.GroupCode is null || offeredGroupCodes.Contains(violation.GroupCode))
                && (violation.RelatedGroupCode is null || reachable.Contains(violation.RelatedGroupCode)))
            .ToList();

        return new DesignEvaluation(
            violations.All(violation => !violation.Blocks),
            violations,
            autoSelections,
            evaluation.Notes);
    }
}
