using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.Modules.Catalog.Api.Payloads;

/// <summary>One design option group as the administration screens read it, with its options.</summary>
public sealed record DesignGroupPayload(
    Guid DesignOptionGroupId,
    Guid CategoryId,
    string Code,
    string Name,
    string? NameTamil,
    string SelectionMode,
    bool Required,
    int DisplayOrder,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    IReadOnlyList<Guid> BranchIds,
    IReadOnlyList<DesignOptionPayload> Options)
{
    /// <summary>Projects a group.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The payload, options in display order.</returns>
    public static DesignGroupPayload From(DesignOptionGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new DesignGroupPayload(
            group.Id,
            group.CategoryId,
            group.Code,
            group.Name,
            group.NameTamil,
            group.SelectionMode.ToString(),
            group.Required,
            group.DisplayOrder,
            group.ActiveFrom,
            group.ActiveTo,
            [.. group.BranchIds.Order()],
            [.. group.Options
                .OrderBy(option => option.DisplayOrder)
                .ThenBy(option => option.Code, StringComparer.Ordinal)
                .Select(DesignOptionPayload.From)]);
    }
}

/// <summary>One design option.</summary>
public sealed record DesignOptionPayload(
    Guid DesignOptionId,
    Guid DesignOptionGroupId,
    string Code,
    string Name,
    string? NameTamil,
    string HelpText,
    string? IllustrationKey,
    string IllustrationAlt,
    string? PriceListItemCode,
    int TimeImpactDays,
    int DisplayOrder,
    bool Active)
{
    /// <summary>Projects an option.</summary>
    /// <param name="option">The option.</param>
    /// <returns>The payload.</returns>
    public static DesignOptionPayload From(DesignOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new DesignOptionPayload(
            option.Id,
            option.DesignOptionGroupId,
            option.Code,
            option.Name,
            option.NameTamil,
            option.HelpText,
            option.IllustrationKey,
            option.IllustrationAlt,
            option.PriceListItemCode,
            option.TimeImpactDays,
            option.DisplayOrder,
            option.Active);
    }
}

/// <summary>One side of a rule, in the operand grammar of the design options document.</summary>
public sealed record DesignOperandPayload(string? GroupCode, string Form, IReadOnlyList<string> OptionCodes)
{
    /// <summary>Projects an operand.</summary>
    /// <param name="operand">The operand.</param>
    /// <returns>The payload.</returns>
    public static DesignOperandPayload From(DesignRuleOperand operand)
    {
        ArgumentNullException.ThrowIfNull(operand);

        return new DesignOperandPayload(operand.GroupCode, operand.Form.ToString(), [.. operand.OptionCodes]);
    }
}

/// <summary>One design rule, with its statement written out.</summary>
public sealed record DesignRulePayload(
    Guid DesignRuleId,
    Guid CategoryId,
    int Number,
    string Identifier,
    string Type,
    DesignOperandPayload Antecedent,
    DesignOperandPayload? Consequent,
    string? Note,
    string? Why,
    string Statement,
    bool Blocks)
{
    /// <summary>Projects a rule.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The payload.</returns>
    public static DesignRulePayload From(DesignRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new DesignRulePayload(
            rule.Id,
            rule.CategoryId,
            rule.Number,
            rule.Identifier,
            rule.Type.ToString(),
            DesignOperandPayload.From(rule.Antecedent),
            rule.Consequent is { } consequent ? DesignOperandPayload.From(consequent) : null,
            rule.Note,
            rule.Why,
            rule.Statement,
            rule.Blocks);
    }
}

/// <summary>What an administrator says about a group.</summary>
public sealed record DesignGroupRequest(
    string? Code,
    string? Name,
    string? NameTamil,
    string? SelectionMode,
    bool Required,
    int DisplayOrder,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    IReadOnlyList<Guid>? BranchIds,
    string? Reason)
{
    /// <summary>The details the domain validates.</summary>
    /// <returns>The details; an unknown selection mode is an undefined value the validation names.</returns>
    public DesignGroupDetails ToDetails()
        => new(
            Code ?? string.Empty,
            Name ?? string.Empty,
            NameTamil,
            Enum.TryParse<DesignSelectionMode>(SelectionMode, ignoreCase: false, out var mode)
                ? mode
                : (DesignSelectionMode)(-1),
            Required,
            DisplayOrder,
            ActiveFrom,
            ActiveTo,
            BranchIds ?? []);
}

/// <summary>What an administrator says about an option.</summary>
public sealed record DesignOptionRequest(
    string? Code,
    string? Name,
    string? NameTamil,
    string? HelpText,
    string? IllustrationKey,
    string? IllustrationAlt,
    string? PriceListItemCode,
    int TimeImpactDays,
    int DisplayOrder,
    bool Active,
    string? Reason)
{
    /// <summary>The details the domain validates.</summary>
    /// <returns>The details.</returns>
    public DesignOptionDetails ToDetails()
        => new(
            Code ?? string.Empty,
            Name ?? string.Empty,
            NameTamil,
            HelpText ?? string.Empty,
            IllustrationKey,
            IllustrationAlt ?? string.Empty,
            PriceListItemCode,
            TimeImpactDays,
            DisplayOrder,
            Active);
}

/// <summary>One side of a rule as an administrator writes it.</summary>
public sealed record DesignOperandRequest(string? GroupCode, string? Form, IReadOnlyList<string>? OptionCodes)
{
    /// <summary>The operand the domain validates.</summary>
    /// <returns>The operand; an unknown form is an undefined value the validation names.</returns>
    public DesignRuleOperand ToOperand()
        => new(
            GroupCode,
            Enum.TryParse<DesignOperandForm>(Form, ignoreCase: false, out var form)
                ? form
                : (DesignOperandForm)(-1),
            OptionCodes ?? []);
}

/// <summary>What an administrator says about a rule.</summary>
public sealed record DesignRuleRequest(
    string? Type,
    DesignOperandRequest? Antecedent,
    DesignOperandRequest? Consequent,
    string? Note,
    string? Why,
    string? Reason)
{
    /// <summary>The details the domain validates.</summary>
    /// <returns>The details.</returns>
    public DesignRuleDetails ToDetails()
        => new(
            Enum.TryParse<DesignRuleType>(Type, ignoreCase: false, out var type)
                ? type
                : (DesignRuleType)(-1),
            Antecedent?.ToOperand() ?? new DesignRuleOperand(null, (DesignOperandForm)(-1), []),
            Consequent?.ToOperand(),
            Note,
            Why);
}

/// <summary>A correction to the words of a published group.</summary>
public sealed record DesignGroupPresentationRequest(
    string? Name,
    string? NameTamil,
    int DisplayOrder,
    string? Reason)
{
    /// <summary>The correction the domain validates.</summary>
    /// <returns>The correction.</returns>
    public DesignGroupPresentation ToPresentation() => new(Name ?? string.Empty, NameTamil, DisplayOrder);
}

/// <summary>A correction to the words of a published option.</summary>
public sealed record DesignOptionPresentationRequest(
    string? Name,
    string? NameTamil,
    string? HelpText,
    string? IllustrationAlt,
    int DisplayOrder,
    string? Reason)
{
    /// <summary>The correction the domain validates.</summary>
    /// <returns>The correction.</returns>
    public DesignOptionPresentation ToPresentation()
        => new(
            Name ?? string.Empty,
            NameTamil,
            HelpText ?? string.Empty,
            IllustrationAlt ?? string.Empty,
            DisplayOrder);
}
