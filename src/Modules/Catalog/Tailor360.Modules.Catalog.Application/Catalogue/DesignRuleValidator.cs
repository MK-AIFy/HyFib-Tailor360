using Tailor360.Modules.Catalog.Contracts.Catalogue;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// The rule-shaped publish checks of <c>docs/prd/design-options.md</c> section 10 (#30, issue #138).
/// </summary>
/// <remarks>
/// <para>
/// The built-in validator checks what one projection can see about shape — codes, words, a rule
/// reading a group its category does not hold. This one reads the rules <em>together</em>: a
/// <c>requires</c> chain that loops, two rules whose option sets overlap, a required group no
/// selection could satisfy, a <c>requires</c> whose every consequent an <c>excludes</c> has removed.
/// Those are the defects an administrator cannot see one rule at a time, which is why each finding
/// names the <c>DR-nn</c> identifiers involved rather than the options: <c>DR-01</c> and <c>DR-06</c>
/// both concern <c>padding = MOULDED_CUP</c>, and a finding that named only the option would be
/// ambiguous between them (section 4 rule 7).
/// </para>
/// <para>
/// Registered as a second <see cref="ICatalogDependencyValidator"/> rather than folded into the first,
/// so that a report attributes each finding to the check that made it and so that this module's own
/// rules go through the same door as another module's.
/// </para>
/// </remarks>
public sealed class DesignRuleValidator : ICatalogDependencyValidator
{
    /// <summary>The name findings are attributed to.</summary>
    public const string ValidatorName = "design";

    /// <summary>The option count above which the tablet picker becomes a scrolling list — proposed, OD-DES-09.</summary>
    public const int ProposedOptionCeiling = 12;

    /// <inheritdoc />
    public string Name => ValidatorName;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<CatalogFinding>> ValidatePublicationAsync(
        CatalogPublicationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var findings = new List<CatalogFinding>();
        var categoryCodes = candidate.Categories.ToDictionary(category => category.Id, category => category.Code);

        foreach (var byCategory in candidate.DesignGroups.GroupBy(group => group.CategoryId))
        {
            if (!categoryCodes.TryGetValue(byCategory.Key, out var categoryCode))
            {
                continue;
            }

            var category = candidate.Categories.Single(found => found.Id == byCategory.Key);
            var groups = byCategory.ToDictionary(group => group.Code, StringComparer.Ordinal);
            var rules = candidate.DesignRules
                .Where(rule => rule.CategoryId == byCategory.Key)
                .OrderBy(rule => rule.Number)
                .ToList();

            CheckRequiredGroups(categoryCode, category, groups, findings);
            CheckOptionCounts(categoryCode, groups, findings);
            CheckRuleReferences(categoryCode, groups, rules, findings);
            CheckOverlaps(groups, rules, findings);
            CheckRequiresCycles(groups, rules, findings);
            CheckRequiresSatisfiable(categoryCode, category, groups, rules, findings);
            CheckRequiresAgainstExcludes(categoryCode, groups, rules, findings);
            CheckRequiredGroupsAgainstExcludes(categoryCode, groups, rules, findings);
        }

        return ValueTask.FromResult<IReadOnlyList<CatalogFinding>>(findings);
    }

    /// <summary>Section 3 rule 1 and section 10: a required group must be satisfiable everywhere its category is offered.</summary>
    private static void CheckRequiredGroups(
        string categoryCode,
        CatalogCategoryView category,
        Dictionary<string, CatalogDesignGroupView> groups,
        List<CatalogFinding> findings)
    {
        foreach (var group in groups.Values.Where(group => group.Required))
        {
            if (group.Options.All(option => !option.Active))
            {
                findings.Add(CatalogFinding.Error(
                    "design.required-group-has-no-active-option",
                    $"'{categoryCode}.{group.Code}' is required, and every one of its options is retired, so no "
                    + "garment of this category could be confirmed.",
                    GroupTarget(categoryCode, group.Code, "options")));
            }

            var missing = category.BranchIds.Except(group.BranchIds).ToArray();
            if (missing.Length > 0)
            {
                findings.Add(CatalogFinding.Error(
                    "design.required-group-not-offered-at-branch",
                    $"'{categoryCode}.{group.Code}' is required, and it is not offered at {missing.Length} of the "
                    + "branch(es) the category is offered at. That would make the category unorderable there "
                    + "without saying so.",
                    GroupTarget(categoryCode, group.Code, "branchIds")));
            }
        }
    }

    private static void CheckOptionCounts(
        string categoryCode,
        Dictionary<string, CatalogDesignGroupView> groups,
        List<CatalogFinding> findings)
    {
        foreach (var group in groups.Values.Where(group => group.Options.Count(option => option.Active) > ProposedOptionCeiling))
        {
            findings.Add(CatalogFinding.Warning(
                "design.group-has-many-options",
                $"'{categoryCode}.{group.Code}' offers {group.Options.Count(option => option.Active)} options, "
                + $"more than the {ProposedOptionCeiling} the tablet picker is thought to hold before it scrolls "
                + "(proposed, OD-DES-09).",
                GroupTarget(categoryCode, group.Code, "options")));
        }
    }

    /// <summary>A rule names options that exist and are not retired; a group it reads is the built-in validator's.</summary>
    private static void CheckRuleReferences(
        string categoryCode,
        Dictionary<string, CatalogDesignGroupView> groups,
        List<CatalogDesignRuleView> rules,
        List<CatalogFinding> findings)
    {
        foreach (var rule in rules)
        {
            foreach (var (side, operand) in Sides(rule))
            {
                if (operand.GroupCode is null || !groups.TryGetValue(operand.GroupCode, out var group))
                {
                    continue;
                }

                var options = group.Options.ToDictionary(option => option.Code, StringComparer.Ordinal);
                foreach (var code in operand.OptionCodes)
                {
                    if (!options.TryGetValue(code, out var option))
                    {
                        findings.Add(CatalogFinding.Error(
                            "design.rule-unknown-option",
                            $"{rule.Identifier} names '{group.Code}.{code}', which is not an option of that group "
                            + "in this version.",
                            RuleTarget(rule.Identifier, $"{side}.optionCodes")));
                    }
                    else if (!option.Active)
                    {
                        findings.Add(CatalogFinding.Error(
                            "design.rule-names-retired-option",
                            $"{rule.Identifier} names '{group.Code}.{code}', which is retired. A rule over an "
                            + "option nobody can choose is either dead or a trap; retire the rule with it.",
                            RuleTarget(rule.Identifier, $"{side}.optionCodes")));
                    }
                }

                if (group.SelectionMode == "SingleChoice"
                    && operand.Form is "Includes" or "Excludes")
                {
                    // A warning, not an error: the document (section 4) describes these two forms for a
                    // multiple-selection group but lists no publish check for them, and over a single
                    // choice they evaluate exactly as `=` and `≠` do. The rule reads oddly; it is not wrong.
                    findings.Add(CatalogFinding.Warning(
                        "design.rule-form-needs-multiple-choice",
                        $"{rule.Identifier} reads '{group.Code}' with '{operand.Form}', which is a form for a "
                        + $"multiple-choice group; '{group.Code}' takes one choice, so '=' or '≠' says the same.",
                        RuleTarget(rule.Identifier, $"{side}.form")));
                }
            }
        }
    }

    /// <summary>
    /// Section 4 rule 3: a rule whose two operand sets overlap — resolved over the group's options, so that
    /// <c>lining = FULL requires lining ≠ NONE</c> is caught as surely as <c>lining = FULL requires lining = FULL</c>.
    /// </summary>
    private static void CheckOverlaps(
        Dictionary<string, CatalogDesignGroupView> groups,
        List<CatalogDesignRuleView> rules,
        List<CatalogFinding> findings)
    {
        foreach (var rule in rules.Where(rule => rule.Consequent is not null))
        {
            var antecedent = rule.Antecedent;
            var consequent = rule.Consequent!;
            if (antecedent.GroupCode is null
                || !string.Equals(antecedent.GroupCode, consequent.GroupCode, StringComparison.Ordinal)
                || !groups.TryGetValue(antecedent.GroupCode, out var group))
            {
                continue;
            }

            var common = SetOf(antecedent, group);
            common.IntersectWith(SetOf(consequent, group));
            if (common.Count > 0)
            {
                findings.Add(CatalogFinding.Error(
                    "design.rule-operands-overlap",
                    $"{rule.Identifier}: both sides of the rule are satisfied by "
                    + $"'{antecedent.GroupCode}.{string.Join(", ", common.Order(StringComparer.Ordinal))}', so the "
                    + "rule either says nothing or contradicts itself.",
                    RuleTarget(rule.Identifier, "consequent.optionCodes")));
            }
        }
    }

    /// <summary>
    /// Section 4 rule 3: a <c>requires</c> cycle. The graph is over <em>options</em>, not groups: an
    /// edge runs from each option the antecedent fires on to each option the consequent obliges, so
    /// <c>lining = FULL requires padding = LIGHT</c> beside <c>padding = MOULDED_CUP requires lining =
    /// KATORI_CUP</c> is two rules over the same two groups and no cycle at all.
    /// </summary>
    private static void CheckRequiresCycles(
        Dictionary<string, CatalogDesignGroupView> groups,
        List<CatalogDesignRuleView> rules,
        List<CatalogFinding> findings)
    {
        var edges = new List<RequiresEdge>();
        foreach (var rule in rules.Where(rule => rule.Type == "Requires" && rule.Consequent?.GroupCode is not null))
        {
            if (rule.Antecedent.GroupCode is not { } fromGroup
                || !groups.TryGetValue(fromGroup, out var from)
                || !groups.TryGetValue(rule.Consequent!.GroupCode!, out var to))
            {
                continue;
            }

            foreach (var fires in SetOf(rule.Antecedent, from))
            {
                foreach (var obliges in SetOf(rule.Consequent, to))
                {
                    edges.Add(new RequiresEdge(rule, (from.Code, fires), (to.Code, obliges)));
                }
            }
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in edges)
        {
            var path = new List<RequiresEdge> { start };
            if (!Reaches(start.To, start.From, edges, path, []))
            {
                continue;
            }

            var identifiers = path.Select(edge => edge.Rule.Identifier).Distinct(StringComparer.Ordinal).ToArray();
            if (reported.Add(string.Join("|", identifiers.Order(StringComparer.Ordinal))))
            {
                var nodes = path.Select(edge => $"{edge.From.Group}.{edge.From.Option}").Append($"{start.From.Group}.{start.From.Option}");
                findings.Add(CatalogFinding.Error(
                    "design.requires-cycle",
                    $"{string.Join(", ", identifiers)} require one another in a circle "
                    + $"({string.Join(" → ", nodes)}), so selecting any of them obliges all of them for no "
                    + "reason a customer chose.",
                    RuleTarget(start.Rule.Identifier, "consequent")));
            }
        }
    }

    private static bool Reaches(
        (string Group, string Option) from,
        (string Group, string Option) target,
        List<RequiresEdge> edges,
        List<RequiresEdge> path,
        HashSet<(string Group, string Option)> visited)
    {
        if (from == target)
        {
            return true;
        }

        if (!visited.Add(from))
        {
            return false;
        }

        foreach (var edge in edges.Where(edge => edge.From == from))
        {
            path.Add(edge);
            if (Reaches(edge.To, target, edges, path, visited))
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    /// <summary>
    /// Section 10: a <c>requires</c> whose consequent set holds no option that is active, or whose
    /// group is not offered — at a branch, or in a season — everywhere its antecedent can fire. An
    /// <c>always</c> antecedent fires wherever and whenever the category is offered.
    /// </summary>
    private static void CheckRequiresSatisfiable(
        string categoryCode,
        CatalogCategoryView category,
        Dictionary<string, CatalogDesignGroupView> groups,
        List<CatalogDesignRuleView> rules,
        List<CatalogFinding> findings)
    {
        foreach (var rule in rules.Where(rule => rule.Type == "Requires" && rule.Consequent?.GroupCode is not null))
        {
            if (!groups.TryGetValue(rule.Consequent!.GroupCode!, out var wantedGroup))
            {
                continue;
            }

            var wanted = SetOf(rule.Consequent, wantedGroup);
            var live = wanted.Where(code => wantedGroup.Options.Any(option => option.Code == code && option.Active)).ToList();
            if (live.Count == 0)
            {
                findings.Add(CatalogFinding.Error(
                    "design.requires-nothing-offerable",
                    $"{rule.Identifier} requires an option of '{wantedGroup.Code}' that is not offered: every option "
                    + "it names is retired.",
                    RuleTarget(rule.Identifier, "consequent.optionCodes")));
                continue;
            }

            // Where and when the antecedent can fire: its own group's reach, or the whole category's
            // for `always`.
            IReadOnlyCollection<Guid> firesAt;
            DateOnly? firesFrom;
            DateOnly? firesTo;
            string firesAs;
            if (rule.Antecedent.GroupCode is { } antecedentGroupCode)
            {
                if (!groups.TryGetValue(antecedentGroupCode, out var antecedentGroup))
                {
                    continue;
                }

                firesAt = antecedentGroup.BranchIds;
                firesFrom = antecedentGroup.ActiveFrom;
                firesTo = antecedentGroup.ActiveTo;
                firesAs = $"'{antecedentGroup.Code}'";
            }
            else
            {
                firesAt = category.BranchIds;
                firesFrom = category.ActiveFrom;
                firesTo = category.ActiveTo;
                firesAs = $"'{categoryCode}'";
            }

            if (firesAt.Except(wantedGroup.BranchIds).ToArray() is { Length: > 0 } elsewhere)
            {
                findings.Add(CatalogFinding.Error(
                    "design.requires-not-offered-where-antecedent-is",
                    $"{rule.Identifier} requires an option of '{wantedGroup.Code}', which is not offered at "
                    + $"{elsewhere.Length} branch(es) where {firesAs} is. A customer there could choose the "
                    + "antecedent and never satisfy the rule.",
                    RuleTarget(rule.Identifier, "consequent.groupCode")));
            }

            if (!Covers(wantedGroup.ActiveFrom, wantedGroup.ActiveTo, firesFrom, firesTo))
            {
                findings.Add(CatalogFinding.Error(
                    "design.requires-not-offered-when-antecedent-is",
                    $"{rule.Identifier} requires an option of '{wantedGroup.Code}', whose active period does not "
                    + $"cover every day {firesAs} is offered. Outside it a customer could choose the antecedent "
                    + "and never satisfy the rule.",
                    RuleTarget(rule.Identifier, "consequent.groupCode")));
            }
        }
    }

    /// <summary>Whether one active period contains another; null is unbounded on that side.</summary>
    private static bool Covers(DateOnly? outerFrom, DateOnly? outerTo, DateOnly? innerFrom, DateOnly? innerTo)
        => (outerFrom is null || (innerFrom is not null && outerFrom <= innerFrom))
           && (outerTo is null || (innerTo is not null && outerTo >= innerTo));

    /// <summary>
    /// Section 4 rule 8: a <c>requires</c> whose consequent set an <c>excludes</c> empties. The pair is a
    /// defect whenever the two can fire on the same garment: any selection that satisfies both antecedents
    /// leaves the customer obliged to choose from a set with nothing left in it.
    /// </summary>
    private static void CheckRequiresAgainstExcludes(
        string categoryCode,
        Dictionary<string, CatalogDesignGroupView> groups,
        List<CatalogDesignRuleView> rules,
        List<CatalogFinding> findings)
    {
        var excludes = rules.Where(rule => rule.Type == "Excludes" && rule.Consequent?.GroupCode is not null).ToList();

        foreach (var requires in rules.Where(rule => rule.Type == "Requires" && rule.Consequent?.GroupCode is not null))
        {
            if (!groups.TryGetValue(requires.Consequent!.GroupCode!, out var group))
            {
                continue;
            }

            var wanted = SetOf(requires.Consequent, group);
            wanted.IntersectWith(LiveOptions(group));
            foreach (var exclude in excludes.Where(exclude =>
                         string.Equals(exclude.Consequent!.GroupCode, requires.Consequent.GroupCode, StringComparison.Ordinal)
                         && !ReferenceEquals(exclude, requires)
                         && CanFireTogether(exclude.Antecedent, requires.Antecedent, groups)))
            {
                var forbidden = SetOf(exclude.Consequent!, group);
                if (wanted.Count > 0 && wanted.All(forbidden.Contains))
                {
                    findings.Add(CatalogFinding.Error(
                        "design.requires-emptied-by-excludes",
                        $"{requires.Identifier} requires {Describe(requires.Consequent)} whenever "
                        + $"{Describe(requires.Antecedent)}, and {exclude.Identifier} excludes every one of those "
                        + "options at the same moment. The pair is the defect; neither rule alone is.",
                        RuleTarget(requires.Identifier, "consequent.optionCodes")));
                    findings.Add(CatalogFinding.Error(
                        "design.requires-emptied-by-excludes",
                        $"{exclude.Identifier} excludes every option {requires.Identifier} requires whenever "
                        + $"{Describe(requires.Antecedent)}.",
                        RuleTarget(exclude.Identifier, "consequent.optionCodes")));
                }
            }
        }
    }

    /// <summary>
    /// Section 4 rule 4: a rule that makes a required group unsatisfiable — for every garment when its
    /// antecedent is <c>always</c>, and for every garment that chooses the antecedent otherwise, which is
    /// as much a trap: the customer picks a sleeve and can no longer confirm anything.
    /// </summary>
    private static void CheckRequiredGroupsAgainstExcludes(
        string categoryCode,
        Dictionary<string, CatalogDesignGroupView> groups,
        List<CatalogDesignRuleView> rules,
        List<CatalogFinding> findings)
    {
        foreach (var exclude in rules.Where(rule => rule.Type == "Excludes" && rule.Consequent?.GroupCode is not null))
        {
            if (!groups.TryGetValue(exclude.Consequent!.GroupCode!, out var group) || !group.Required)
            {
                continue;
            }

            var live = LiveOptions(group);
            var forbidden = SetOf(exclude.Consequent, group);
            if (live.Count > 0 && live.All(forbidden.Contains))
            {
                var when = exclude.Antecedent.Form == "Always"
                    ? "for every garment"
                    : $"whenever {Describe(exclude.Antecedent)}";
                findings.Add(CatalogFinding.Error(
                    "design.required-group-unsatisfiable",
                    $"{exclude.Identifier} excludes every option of '{categoryCode}.{group.Code}' {when}, and the "
                    + "group is required, so no such garment could be confirmed.",
                    RuleTarget(exclude.Identifier, "consequent.optionCodes")));
            }
        }
    }

    /// <summary>
    /// Whether two antecedents can hold on one garment. Over different groups they can, because the
    /// groups are chosen independently; over the same group, when some single choice satisfies both, or
    /// — for a multiple-choice group — unless one names an option the other forbids; <c>always</c> holds
    /// with anything.
    /// </summary>
    private static bool CanFireTogether(
        CatalogDesignOperandView first,
        CatalogDesignOperandView second,
        Dictionary<string, CatalogDesignGroupView> groups)
    {
        if (first.Form == "Always" || second.Form == "Always"
            || first.GroupCode is null || second.GroupCode is null
            || !string.Equals(first.GroupCode, second.GroupCode, StringComparison.Ordinal)
            || !groups.TryGetValue(first.GroupCode, out var group))
        {
            return true;
        }

        if (group.SelectionMode == "MultipleChoice")
        {
            return !(IsPositive(first) && second.Form == "Excludes" && first.OptionCodes.Contains(second.OptionCodes[0], StringComparer.Ordinal))
                   && !(IsPositive(second) && first.Form == "Excludes" && second.OptionCodes.Contains(first.OptionCodes[0], StringComparer.Ordinal));
        }

        var common = SetOf(first, group);
        common.IntersectWith(SetOf(second, group));
        return common.Count > 0;
    }

    private static bool IsPositive(CatalogDesignOperandView operand) => operand.Form is "Equals" or "In" or "Includes";

    /// <summary>The options of the group that satisfy the operand, read as a single choice (<c>DesignRuleOperand.SatisfyingSet</c> over the view).</summary>
    private static HashSet<string> SetOf(CatalogDesignOperandView operand, CatalogDesignGroupView group)
    {
        var all = group.Options.Select(option => option.Code).ToHashSet(StringComparer.Ordinal);
        return operand.Form switch
        {
            "Equals" or "In" or "Includes" => operand.OptionCodes.ToHashSet(StringComparer.Ordinal),
            "NotEquals" or "Excludes" => all.Except(operand.OptionCodes, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
            _ => all,
        };
    }

    private static HashSet<string> LiveOptions(CatalogDesignGroupView group)
        => group.Options.Where(option => option.Active).Select(option => option.Code).ToHashSet(StringComparer.Ordinal);

    private sealed record RequiresEdge(
        CatalogDesignRuleView Rule,
        (string Group, string Option) From,
        (string Group, string Option) To);

    private static string Describe(CatalogDesignOperandView operand)
    {
        var first = operand.OptionCodes.Count == 0 ? string.Empty : operand.OptionCodes[0];

        return operand.Form switch
        {
            "Always" => "always",
            "AnySelection" => $"any selection in {operand.GroupCode}",
            "Equals" => $"{operand.GroupCode} = {first}",
            "NotEquals" => $"{operand.GroupCode} ≠ {first}",
            "Includes" => $"{operand.GroupCode} includes {first}",
            "Excludes" => $"{operand.GroupCode} excludes {first}",
            "In" => $"{operand.GroupCode} in ({string.Join(", ", operand.OptionCodes)})",
            _ => $"{operand.GroupCode} ?",
        };
    }

    private static IEnumerable<(string Side, CatalogDesignOperandView Operand)> Sides(CatalogDesignRuleView rule)
    {
        yield return ("antecedent", rule.Antecedent);
        if (rule.Consequent is { } consequent)
        {
            yield return ("consequent", consequent);
        }
    }

    private static string GroupTarget(string categoryCode, string groupCode, string field)
        => $"designGroups[{categoryCode}.{groupCode}].{field}";

    private static string RuleTarget(string identifier, string field) => $"designRules[{identifier}].{field}";
}
