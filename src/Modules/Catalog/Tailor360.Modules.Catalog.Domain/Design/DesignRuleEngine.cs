namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// The rule engine of <c>docs/prd/design-options.md</c> section 4: pure, deterministic, stateless.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Evaluation runs in a fixed order</strong> (section 4 rule 8), so that two implementations
/// cannot reach different answers for the same selection set: what is chosen is first checked
/// against what is offerable; <c>excludes</c> then narrows each group's admissible set; <c>requires</c>
/// is resolved against the narrowed sets to a fixed point, transitive by evaluation; a
/// <c>requires attachment</c> reads the garment; and notes are attached last.
/// </para>
/// <para>
/// <strong>A condition over an unset group is false, negations included</strong> (section 8). DR-16's
/// <c>dupatta_finish ≠ NONE</c> does not fire on a garment with nothing chosen in that group; it fires
/// once a finish has been chosen. Nothing here pre-selects on the customer's behalf, with one exception
/// the document makes: a <c>requires</c> whose consequent set holds <em>exactly one</em> admissible
/// option is satisfied by selecting it and saying so (rule 9); two or more is a prompt, never a choice.
/// </para>
/// <para>
/// <strong>What a rule added is checked as if the customer had chosen it.</strong> After the
/// <c>requires</c> fixed point, every <c>excludes</c> is read once more over the selections as they
/// now stand, so an option a rule selected can never slip past a rule that forbids it — the publish
/// checks make that pair an error, but a version published before the check existed is still read
/// safely.
/// </para>
/// </remarks>
public static class DesignRuleEngine
{
    /// <summary>Evaluates one garment's selections against the groups and rules of its category.</summary>
    /// <param name="groups">The category's groups in this version, retired options included.</param>
    /// <param name="rules">The category's rules in this version.</param>
    /// <param name="selections">What was chosen; a group not listed is unset.</param>
    /// <param name="branchId">The branch the garment is ordered at.</param>
    /// <param name="on">The day, in the branch's timezone.</param>
    /// <param name="hasReferenceImage">Whether the garment holds a reference image.</param>
    /// <returns>The evaluation.</returns>
    public static DesignEvaluationResult Evaluate(
        IReadOnlyCollection<DesignOptionGroup> groups,
        IReadOnlyCollection<DesignRule> rules,
        IReadOnlyList<DesignSelectionInput> selections,
        Guid branchId,
        DateOnly on,
        bool hasReferenceImage)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(selections);

        var violations = new List<DesignViolationResult>();
        var autoSelections = new List<DesignAutoSelectionResult>();
        var notes = new List<DesignNoteResult>();

        var byCode = groups.ToDictionary(group => group.Code, StringComparer.Ordinal);
        var chosen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // 1. What was chosen, against what exists and what is offerable here today. In group order, so
        // that the order the caller listed the groups in cannot change the order of the answer.
        foreach (var selection in selections.OrderBy(selection => selection.GroupCode, StringComparer.Ordinal))
        {
            if (!byCode.TryGetValue(selection.GroupCode, out var group))
            {
                violations.Add(Violation(
                    "design.unknown-group", null, selection.GroupCode,
                    [.. selection.OptionCodes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], null, [],
                    $"'{selection.GroupCode}' is not a design group of this category in this catalogue version."));
                continue;
            }

            var set = chosen.TryGetValue(group.Code, out var existing) ? existing : chosen[group.Code] = [];
            foreach (var code in selection.OptionCodes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (group.FindOptionByCode(code) is not { } option)
                {
                    violations.Add(Violation(
                        "design.unknown-option", null, group.Code, [code], null, [],
                        $"'{group.Code}.{code}' is not an option of this group in this catalogue version."));
                    continue;
                }

                if (!IsOfferable(group, option, branchId, on))
                {
                    violations.Add(Violation(
                        "design.option-not-offerable", null, group.Code, [code], null, [],
                        $"'{group.Code}.{code}' is not offered at this branch today: it is retired, outside "
                        + "its active period, or offered elsewhere."));
                    continue;
                }

                set.Add(code);
            }

            if (group.SelectionMode == DesignSelectionMode.SingleChoice && set.Count > 1)
            {
                violations.Add(Violation(
                    "design.too-many-selected", null, group.Code, [.. set.Order(StringComparer.Ordinal)], null, [],
                    $"'{group.Code}' takes one choice, and {set.Count} were made."));
            }
        }

        // The admissible options of every group: offerable, and not yet excluded by a rule.
        var admissible = byCode.Values.ToDictionary(
            group => group.Code,
            group => group.Options
                .Where(option => IsOfferable(group, option, branchId, on))
                .Select(option => option.Code)
                .ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        var ordered = rules.OrderBy(rule => rule.Number).ToList();

        // 2. Excludes narrows each group's admissible set first.
        var excluded = new HashSet<(Guid Rule, string Option)>();
        foreach (var rule in ordered.Where(rule => rule.Type == DesignRuleType.Excludes))
        {
            if (!Holds(rule.Antecedent, chosen) || rule.Consequent is not { GroupCode: { } forbiddenGroup } consequent)
            {
                continue;
            }

            var forbidden = OptionSetOf(consequent, byCode);
            if (admissible.TryGetValue(forbiddenGroup, out var set))
            {
                set.ExceptWith(forbidden);
            }

            if (chosen.TryGetValue(forbiddenGroup, out var selected)
                && selected.Intersect(forbidden, StringComparer.Ordinal).ToArray() is { Length: > 0 } clash)
            {
                excluded.UnionWith(clash.Select(code => (rule.Id, code)));
                violations.Add(Violation(
                    "design.excluded", rule.Identifier, forbiddenGroup, [.. clash.Order(StringComparer.Ordinal)],
                    rule.Antecedent.GroupCode, [.. rule.Antecedent.OptionCodes],
                    $"{rule.Identifier}: {rule.Antecedent} excludes {consequent}, and both are chosen."));
            }
        }

        // 3. Requires, to a fixed point: a selection a rule adds can be what another rule reads.
        var effective = chosen.ToDictionary(
            pair => pair.Key, pair => new HashSet<string>(pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        var settled = new HashSet<Guid>();
        bool changed;
        do
        {
            // Terminates: a pass either settles at least one more rule or changes nothing.
            changed = false;
            foreach (var rule in ordered.Where(rule => rule.Type == DesignRuleType.Requires && !settled.Contains(rule.Id)))
            {
                if (!Holds(rule.Antecedent, effective)
                    || rule.Consequent is not { GroupCode: { } requiredGroup } consequent)
                {
                    continue;
                }

                settled.Add(rule.Id);
                var wanted = OptionSetOf(consequent, byCode);
                var already = effective.TryGetValue(requiredGroup, out var selected)
                              && SatisfiedBy(consequent, selected);
                if (already)
                {
                    continue;
                }

                var candidates = admissible.TryGetValue(requiredGroup, out var pool)
                    ? wanted.Where(pool.Contains).Order(StringComparer.Ordinal).ToArray()
                    : [];
                var current = effective.TryGetValue(requiredGroup, out var held) ? held : effective[requiredGroup] = [];
                var holdsAnother = current.Count > 0
                                   && byCode[requiredGroup].SelectionMode == DesignSelectionMode.SingleChoice;

                if (candidates.Length == 1 && !holdsAnother)
                {
                    // Rule 9: exactly one option can satisfy it, so it is selected on the customer's behalf
                    // and said.
                    var pick = candidates[0];
                    current.Add(pick);
                    autoSelections.Add(new DesignAutoSelectionResult(rule.Identifier, requiredGroup, pick));
                    changed = true;
                    continue;
                }

                if (candidates.Length == 0)
                {
                    violations.Add(Violation(
                        "design.requires-unsatisfiable", rule.Identifier, requiredGroup, [.. wanted.Order(StringComparer.Ordinal)],
                        rule.Antecedent.GroupCode, [.. rule.Antecedent.OptionCodes],
                        $"{rule.Identifier}: {rule.Antecedent} requires {consequent}, and none of those options "
                        + "can be chosen here today."));
                }
                else if (holdsAnother)
                {
                    // A single-choice group already holding another value is never overwritten: that would
                    // be two decisions made silently. It is said as a conflict, not as a prompt to choose.
                    violations.Add(Violation(
                        "design.requires-conflict", rule.Identifier, requiredGroup, [.. current.Order(StringComparer.Ordinal)],
                        rule.Antecedent.GroupCode, [.. rule.Antecedent.OptionCodes],
                        $"{rule.Identifier}: {rule.Antecedent} requires {consequent}, and '{requiredGroup}' holds "
                        + $"{string.Join(", ", current.Order(StringComparer.Ordinal))} instead."));
                }
                else
                {
                    violations.Add(Violation(
                        "design.requires-choice", rule.Identifier, requiredGroup, [.. candidates],
                        rule.Antecedent.GroupCode, [.. rule.Antecedent.OptionCodes],
                        $"{rule.Identifier}: {rule.Antecedent} requires {consequent}. Choose one of them."));
                }
            }
        }
        while (changed);

        // 3b. Excludes once more, over what the rules added: an auto-selected option that another rule
        // forbids, or a selection an auto-selection has now made forbidden, is a clash like any other.
        foreach (var rule in ordered.Where(rule => rule.Type == DesignRuleType.Excludes))
        {
            if (!Holds(rule.Antecedent, effective) || rule.Consequent is not { GroupCode: { } forbiddenGroup } consequent
                || !effective.TryGetValue(forbiddenGroup, out var selected))
            {
                continue;
            }

            var clash = selected.Intersect(OptionSetOf(consequent, byCode), StringComparer.Ordinal)
                .Where(code => !excluded.Contains((rule.Id, code)))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (clash.Length > 0)
            {
                violations.Add(Violation(
                    "design.excluded", rule.Identifier, forbiddenGroup, clash,
                    rule.Antecedent.GroupCode, [.. rule.Antecedent.OptionCodes],
                    $"{rule.Identifier}: {rule.Antecedent} excludes {consequent}, and a rule selected one side "
                    + "of it on the customer's behalf."));
            }
        }

        // 4. A required group with nothing in it, after what the rules added.
        foreach (var group in byCode.Values.Where(group => group.Required && IsOfferable(group, branchId, on))
                     .OrderBy(group => group.DisplayOrder).ThenBy(group => group.Code, StringComparer.Ordinal))
        {
            if (!effective.TryGetValue(group.Code, out var set) || set.Count == 0)
            {
                violations.Add(Violation(
                    "design.required-group-unset", null, group.Code, [], null, [],
                    $"'{group.Code}' needs a choice before the garment can be confirmed."));
            }
        }

        // 5. A reference image, where a rule asks for one.
        foreach (var rule in ordered.Where(rule => rule.Type == DesignRuleType.RequiresAttachment))
        {
            if (Holds(rule.Antecedent, effective) && !hasReferenceImage)
            {
                violations.Add(Violation(
                    "design.reference-image-required", rule.Identifier, rule.Antecedent.GroupCode,
                    [.. rule.Antecedent.OptionCodes], null, [],
                    $"{rule.Identifier}: {rule.Antecedent} requires a reference image on the garment."));
            }
        }

        // 6. Notes last, and never blocking.
        foreach (var rule in ordered.Where(rule => rule.Type == DesignRuleType.Note))
        {
            if (Holds(rule.Antecedent, effective) && rule.Note is { } text)
            {
                notes.Add(new DesignNoteResult(rule.Identifier, text));
            }
        }

        return new DesignEvaluationResult(
            violations.All(violation => !violation.Blocks), violations, autoSelections, notes);
    }

    /// <summary>Whether an operand holds against a selection set. An unset group makes every form but <c>always</c> false.</summary>
    private static bool Holds(DesignRuleOperand operand, Dictionary<string, HashSet<string>> selections)
    {
        if (operand.Form == DesignOperandForm.Always)
        {
            return true;
        }

        if (operand.GroupCode is null
            || !selections.TryGetValue(operand.GroupCode, out var set)
            || set.Count == 0)
        {
            return false;
        }

        return SatisfiedBy(operand, set);
    }

    private static bool SatisfiedBy(DesignRuleOperand operand, HashSet<string> set)
        => operand.Form switch
        {
            DesignOperandForm.Always => true,
            DesignOperandForm.AnySelection => set.Count > 0,
            DesignOperandForm.Equals => set.Count == 1 && set.Contains(operand.OptionCodes[0]),
            DesignOperandForm.NotEquals => set.Count > 0 && !(set.Count == 1 && set.Contains(operand.OptionCodes[0])),
            DesignOperandForm.In => set.Overlaps(operand.OptionCodes),
            DesignOperandForm.Includes => set.Contains(operand.OptionCodes[0]),
            DesignOperandForm.Excludes => set.Count > 0 && !set.Contains(operand.OptionCodes[0]),
            _ => false,
        };

    /// <summary>The options a consequent names, as the set of codes that would satisfy it.</summary>
    private static HashSet<string> OptionSetOf(DesignRuleOperand consequent, Dictionary<string, DesignOptionGroup> groups)
        => consequent.GroupCode is { } code && groups.TryGetValue(code, out var group)
            ? consequent.SatisfyingSet(group.Options.Select(option => option.Code))
            : new HashSet<string>(StringComparer.Ordinal);

    private static bool IsOfferable(DesignOptionGroup group, DesignOption option, Guid branchId, DateOnly on)
        => option.Active && IsOfferable(group, branchId, on);

    private static bool IsOfferable(DesignOptionGroup group, Guid branchId, DateOnly on)
        => group.IsActiveOn(on) && group.BranchIds.Contains(branchId);

    private static DesignViolationResult Violation(
        string code,
        string? rule,
        string? groupCode,
        IReadOnlyList<string> optionCodes,
        string? relatedGroupCode,
        IReadOnlyList<string> relatedOptionCodes,
        string message)
        => new(code, rule, groupCode, optionCodes, relatedGroupCode, relatedOptionCodes, message, true);
}

/// <summary>The options chosen in one group, as the engine takes them.</summary>
/// <param name="GroupCode">The group.</param>
/// <param name="OptionCodes">The options chosen.</param>
public sealed record DesignSelectionInput(string GroupCode, IReadOnlyList<string> OptionCodes);

/// <summary>What the engine made of a selection set.</summary>
/// <param name="IsConfirmable">Whether nothing blocking stands.</param>
/// <param name="Violations">Every violation, in evaluation order.</param>
/// <param name="AutoSelections">What a rule selected on the customer's behalf.</param>
/// <param name="Notes">The notes the selections attached.</param>
public sealed record DesignEvaluationResult(
    bool IsConfirmable,
    IReadOnlyList<DesignViolationResult> Violations,
    IReadOnlyList<DesignAutoSelectionResult> AutoSelections,
    IReadOnlyList<DesignNoteResult> Notes);

/// <summary>One thing wrong with a selection set.</summary>
public sealed record DesignViolationResult(
    string Code,
    string? RuleIdentifier,
    string? GroupCode,
    IReadOnlyList<string> OptionCodes,
    string? RelatedGroupCode,
    IReadOnlyList<string> RelatedOptionCodes,
    string Message,
    bool Blocks);

/// <summary>An option a rule selected because it was the only one that could satisfy it.</summary>
public sealed record DesignAutoSelectionResult(string RuleIdentifier, string GroupCode, string OptionCode);

/// <summary>A standing instruction a selection attached.</summary>
public sealed record DesignNoteResult(string RuleIdentifier, string Text);
