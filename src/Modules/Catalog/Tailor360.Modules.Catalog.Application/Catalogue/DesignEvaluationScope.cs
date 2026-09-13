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
/// groups (<see cref="ServiceType.DesignOptionGroupIds"/>). A required group, a rule's consequent, an
/// attachment demand or a note that belongs only to a sibling service is not this draft's business: none
/// of it may gate this draft's confirmability or reach its snapshot.
/// </para>
/// <para>
/// <strong>Every violation, auto-selection and note is the effect of a rule, and the same question
/// decides whether each belongs to this draft: is the rule's own antecedent reachable from what this
/// service's picker showed, not merely from what the category-wide engine happened to compute?</strong> A
/// chain — an offered group requiring an unoffered sibling-service group, which itself requires a second
/// offered group, an attachment, or a note — settles every later step on the strength of a rule that
/// fired only because of the first; checking only the group each output names, or only one hop back,
/// misses that (Codex review, PR #221, three rounds). This walks every rule's antecedent to a fixed
/// point instead, using the operand semantics <see cref="DesignRuleOperand"/> itself documents, and
/// tracks the specific option values a group holds rather than only whether the group is present — a
/// multi-choice group can hold one option this service's picker offered and another only an out-of-scope
/// chain added, and only the first is this draft's.
/// </para>
/// <para>
/// <see cref="Holds"/> mirrors <c>DesignRuleEngine.SatisfiedBy</c>'s private logic against the same
/// public <see cref="DesignOperandForm"/> values that engine's own doc comments fix — not a second
/// opinion on what a rule decides, since every fact it is applied to here already came from that one
/// evaluation; only the narrower question of whether this draft's own picker could ever have produced
/// the values a fact's rule depends on.
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
    /// Keeps only what is reachable from this draft's own service. Confirmability is recomputed from
    /// what remains.
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

        // Seeded with what the draft explicitly holds — always in scope, since Save refuses a value for a
        // group the service does not link — and grown one auto-selected option at a time, only once the
        // rule that added it is itself shown to fire against what is already here. Values, not group
        // codes alone: a group holding any reachable value at all is not the same as holding the specific
        // value a later rule's antecedent depends on.
        var reachable = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var input in explicitInputs)
        {
            var set = reachable.TryGetValue(input.GroupCode, out var existing)
                ? existing
                : reachable[input.GroupCode] = new HashSet<string>(StringComparer.Ordinal);
            foreach (var code in input.OptionCodes)
            {
                set.Add(code);
            }
        }

        bool AntecedentReachable(string? ruleIdentifier)
            => ruleIdentifier is null
                || (rulesByIdentifier.TryGetValue(ruleIdentifier, out var rule) && Holds(rule.Antecedent, reachable));

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

                if (!AntecedentReachable(auto.RuleIdentifier))
                {
                    continue;
                }

                autoSelections.Add(auto);
                var set = reachable.TryGetValue(auto.GroupCode, out var existing)
                    ? existing
                    : reachable[auto.GroupCode] = new HashSet<string>(StringComparer.Ordinal);
                set.Add(auto.OptionCode);
                candidates.RemoveAt(i);
                settledOneMore = true;
            }
        }
        while (settledOneMore && candidates.Count > 0);

        // A violation with no rule behind it (design.required-group-unset, an unknown group or option)
        // has no antecedent to chase — it is judged on the group it names alone. One that does carry a
        // rule (requires, excludes, requires-attachment) is dropped the moment that rule's own antecedent
        // is not itself something this draft's picker reached, whichever field the violation happens to
        // place the antecedent's group in.
        var violations = evaluation.Violations
            .Where(violation =>
                (violation.GroupCode is null || offeredGroupCodes.Contains(violation.GroupCode))
                && AntecedentReachable(violation.RuleIdentifier))
            .ToList();

        var notes = evaluation.Notes.Where(note => AntecedentReachable(note.RuleIdentifier)).ToList();

        return new DesignEvaluation(
            violations.All(violation => !violation.Blocks),
            violations,
            autoSelections,
            notes);
    }

    /// <summary>
    /// Whether an antecedent holds against a set of values known reachable, mirroring
    /// <c>DesignRuleEngine.SatisfiedBy</c>'s private logic against the same <see cref="DesignOperandForm"/>
    /// values that type's own doc comments fix. An unset group makes every form but <c>always</c> false,
    /// the same rule <c>docs/prd/design-options.md</c> section 8 gives the engine itself.
    /// </summary>
    private static bool Holds(DesignRuleOperand antecedent, Dictionary<string, HashSet<string>> reachable)
    {
        if (antecedent.Form == DesignOperandForm.Always)
        {
            return true;
        }

        if (antecedent.GroupCode is null
            || !reachable.TryGetValue(antecedent.GroupCode, out var held)
            || held.Count == 0)
        {
            return false;
        }

        return antecedent.Form switch
        {
            DesignOperandForm.AnySelection => true,
            DesignOperandForm.Equals => held.Count == 1 && held.Contains(antecedent.OptionCodes[0]),
            DesignOperandForm.NotEquals => !(held.Count == 1 && held.Contains(antecedent.OptionCodes[0])),
            DesignOperandForm.In => held.Overlaps(antecedent.OptionCodes),
            DesignOperandForm.Includes => held.Contains(antecedent.OptionCodes[0]),
            DesignOperandForm.Excludes => !held.Contains(antecedent.OptionCodes[0]),
            _ => false,
        };
    }
}
