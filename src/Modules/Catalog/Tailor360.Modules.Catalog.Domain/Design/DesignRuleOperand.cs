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
