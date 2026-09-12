using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Domain.Design;

/// <summary>
/// One side of a rule: a condition over one group of the category, in one of the fixed forms.
/// </summary>
/// <param name="GroupCode">The group the condition reads, or null only for <see cref="DesignOperandForm.Always"/>.</param>
/// <param name="Form">Which of the fixed forms this is.</param>
/// <param name="OptionCodes">The option codes the form names: none, one or several, as the form requires.</param>
public sealed record DesignRuleOperand(
    string? GroupCode,
    DesignOperandForm Form,
    IReadOnlyList<string> OptionCodes)
{
    /// <summary>The unconditional operand.</summary>
    public static DesignRuleOperand Always { get; } = new(null, DesignOperandForm.Always, []);

    /// <summary>
    /// Whether this operand names a set of options over a group, as a consequent must. Every form but
    /// <c>always</c> and <c>any selection</c> does — a negation included, because the document's own
    /// rules oblige <c>lining ≠ NONE</c> (DR-01) and forbid <c>sleeve_style other than SLEEVELESS</c>
    /// (DR-25).
    /// </summary>
    public bool IsOptionSet
        => Form is not (DesignOperandForm.Always or DesignOperandForm.AnySelection);

    /// <summary>
    /// Whether the form names the options that satisfy it directly (<c>=</c>, <c>in</c>, <c>includes</c>)
    /// rather than the one that does not (<c>≠</c>, <c>excludes</c>).
    /// </summary>
    public bool IsPositive
        => Form is DesignOperandForm.Equals or DesignOperandForm.In or DesignOperandForm.Includes;

    /// <summary>Whether the form names the one option that fails it.</summary>
    public bool IsNegation
        => Form is DesignOperandForm.NotEquals or DesignOperandForm.Excludes;

    /// <summary>
    /// The options of the group that satisfy this operand, read as a single choice: the named ones for a
    /// positive form, every other one for a negation, all of them for <c>any selection</c>.
    /// </summary>
    /// <remarks>
    /// This is the set a consequent obliges or forbids, and the set an antecedent fires on; it is what
    /// makes <c>lining ≠ NONE</c> and <c>lining in (FULL, KATORI_CUP)</c> the same rule over a group
    /// holding those three options. <c>always</c> has no group and yields the whole universe.
    /// </remarks>
    /// <param name="universe">Every option code of the group, retired ones included.</param>
    /// <returns>The satisfying codes.</returns>
    public HashSet<string> SatisfyingSet(IEnumerable<string> universe)
    {
        ArgumentNullException.ThrowIfNull(universe);

        var all = universe.ToHashSet(StringComparer.Ordinal);
        if (IsPositive)
        {
            return OptionCodes.ToHashSet(StringComparer.Ordinal);
        }

        if (IsNegation)
        {
            all.ExceptWith(OptionCodes);
        }

        return all;
    }

    /// <summary>
    /// Whether this operand and another over the same group are certainly satisfied by a common option,
    /// decided from the two forms alone. What can be decided without the group's option list: two
    /// positive forms naming a common option; a positive form naming an option a negation does not
    /// exclude; and <c>any selection</c>, which agrees with everything. Two negations cannot be decided
    /// here — over two options they are disjoint, over three they share one — so they are left to the
    /// exact check, <see cref="SatisfyingSet"/> on both sides over the group's options.
    /// </summary>
    /// <param name="other">The other operand.</param>
    /// <returns>True when a common option certainly exists.</returns>
    public bool CertainlyOverlaps(DesignRuleOperand other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Form == DesignOperandForm.Always || other.Form == DesignOperandForm.Always
            || !string.Equals(GroupCode, other.GroupCode, StringComparison.Ordinal))
        {
            return false;
        }

        if (Form == DesignOperandForm.AnySelection || other.Form == DesignOperandForm.AnySelection)
        {
            return true;
        }

        if (IsPositive && other.IsPositive)
        {
            return OptionCodes.Intersect(other.OptionCodes, StringComparer.Ordinal).Any();
        }

        if (IsPositive)
        {
            return OptionCodes.Except(other.OptionCodes, StringComparer.Ordinal).Any();
        }

        return other.IsPositive && other.OptionCodes.Except(OptionCodes, StringComparer.Ordinal).Any();
    }

    /// <summary>Checks the operand against its form.</summary>
    /// <param name="field">The request field the operand came from, for the problem detail.</param>
    /// <returns>Success, or the first thing wrong with it.</returns>
    public Result Validate(string field)
    {
        if (!Enum.IsDefined(Form))
        {
            return Result.Failure(CatalogErrors.OperandMalformed(
                $"{field}.form",
                "The form is one of Equals, NotEquals, In, Includes, Excludes, AnySelection or Always."));
        }

        if (Form == DesignOperandForm.Always)
        {
            return GroupCode is null && OptionCodes.Count == 0
                ? Result.Success()
                : Result.Failure(CatalogErrors.OperandMalformed(
                    field, "'always' names no group and no option."));
        }

        if (!DesignCode.IsWellFormedGroupCode(GroupCode))
        {
            return Result.Failure(CatalogErrors.GroupCodeNotWellFormed($"{field}.groupCode"));
        }

        if (OptionCodes.Any(code => !DesignCode.IsWellFormedOptionCode(code)))
        {
            return Result.Failure(CatalogErrors.OptionCodeNotWellFormed($"{field}.optionCodes"));
        }

        if (OptionCodes.Distinct(StringComparer.Ordinal).Count() != OptionCodes.Count)
        {
            return Result.Failure(CatalogErrors.OperandMalformed(
                $"{field}.optionCodes", "An option is listed twice."));
        }

        return Form switch
        {
            DesignOperandForm.AnySelection when OptionCodes.Count != 0 => Result.Failure(
                CatalogErrors.OperandMalformed(
                    $"{field}.optionCodes", "'any selection' names the group and no option.")),
            DesignOperandForm.In when OptionCodes.Count == 0 => Result.Failure(
                CatalogErrors.OperandMalformed(
                    $"{field}.optionCodes", "'in' lists at least one option.")),
            DesignOperandForm.Equals or DesignOperandForm.NotEquals
                or DesignOperandForm.Includes or DesignOperandForm.Excludes
                when OptionCodes.Count != 1 => Result.Failure(
                    CatalogErrors.OperandMalformed(
                        $"{field}.optionCodes", "This form names exactly one option.")),
            _ => Result.Success(),
        };
    }

    /// <summary>The operand as the document writes it, for audit summaries and findings.</summary>
    /// <returns>For example <c>padding in (LIGHT, MOULDED_CUP)</c>.</returns>
    public override string ToString()
    {
        var first = OptionCodes.Count == 0 ? string.Empty : OptionCodes[0];

        return Form switch
        {
            DesignOperandForm.Always => "always",
            DesignOperandForm.AnySelection => $"any selection in {GroupCode}",
            DesignOperandForm.Equals => $"{GroupCode} = {first}",
            DesignOperandForm.NotEquals => $"{GroupCode} ≠ {first}",
            DesignOperandForm.Includes => $"{GroupCode} includes {first}",
            DesignOperandForm.Excludes => $"{GroupCode} excludes {first}",
            DesignOperandForm.In => $"{GroupCode} in ({string.Join(", ", OptionCodes)})",
            _ => $"{GroupCode} ?",
        };
    }
}
