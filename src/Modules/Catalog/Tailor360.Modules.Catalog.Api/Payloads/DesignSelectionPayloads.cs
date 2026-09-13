using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;

namespace Tailor360.Modules.Catalog.Api.Payloads;

/// <summary>What the picker reads for one service type, at the caller's branch, today.</summary>
public sealed record DesignPickerPayload(
    Guid CatalogVersionId,
    Guid CategoryId,
    Guid ServiceTypeId,
    IReadOnlyList<DesignPickerGroupPayload> Groups,
    IReadOnlyList<DesignPickerRulePayload> Rules)
{
    /// <summary>Projects a picker read.</summary>
    /// <param name="read">The read.</param>
    /// <returns>The payload.</returns>
    public static DesignPickerPayload From(DesignPickerRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        return new DesignPickerPayload(
            read.CatalogVersionId,
            read.CategoryId,
            read.ServiceTypeId,
            [.. read.Groups.Select(DesignPickerGroupPayload.From)],
            [.. read.Rules.OrderBy(rule => rule.Number).Select(DesignPickerRulePayload.From)]);
    }
}

/// <summary>One design option group, as the picker offers it — options filtered to what is active.</summary>
public sealed record DesignPickerGroupPayload(
    Guid DesignOptionGroupId,
    string Code,
    string Name,
    string? NameTamil,
    string SelectionMode,
    bool Required,
    int DisplayOrder,
    IReadOnlyList<DesignPickerOptionPayload> Options)
{
    /// <summary>Projects a group, already narrowed to what is offerable at the caller's branch today.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The payload, active options in display order.</returns>
    public static DesignPickerGroupPayload From(DesignOptionGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new DesignPickerGroupPayload(
            group.Id,
            group.Code,
            group.Name,
            group.NameTamil,
            group.SelectionMode.ToString(),
            group.Required,
            group.DisplayOrder,
            [.. group.Options
                .Where(option => option.Active)
                .OrderBy(option => option.DisplayOrder)
                .ThenBy(option => option.Code, StringComparer.Ordinal)
                .Select(DesignPickerOptionPayload.From)]);
    }
}

/// <summary>One option, as the picker offers it. Retired options are never sent here.</summary>
public sealed record DesignPickerOptionPayload(
    Guid DesignOptionId,
    string Code,
    string Name,
    string? NameTamil,
    string HelpText,
    string? IllustrationKey,
    string IllustrationAlt,
    string? PriceListItemCode,
    int TimeImpactDays,
    int DisplayOrder)
{
    /// <summary>Projects an option.</summary>
    /// <param name="option">The option.</param>
    /// <returns>The payload.</returns>
    public static DesignPickerOptionPayload From(DesignOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new DesignPickerOptionPayload(
            option.Id,
            option.Code,
            option.Name,
            option.NameTamil,
            option.HelpText,
            option.IllustrationKey,
            option.IllustrationAlt,
            option.PriceListItemCode,
            option.TimeImpactDays,
            option.DisplayOrder);
    }
}

/// <summary>One rule, in the client-evaluable form the picker reads it in — the same grammar the admin screens use.</summary>
public sealed record DesignPickerRulePayload(
    string Identifier,
    string Type,
    DesignOperandPayload Antecedent,
    DesignOperandPayload? Consequent,
    string? Note,
    bool Blocks)
{
    /// <summary>Projects a rule.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The payload.</returns>
    public static DesignPickerRulePayload From(DesignRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new DesignPickerRulePayload(
            rule.Identifier,
            rule.Type.ToString(),
            DesignOperandPayload.From(rule.Antecedent),
            rule.Consequent is { } consequent ? DesignOperandPayload.From(consequent) : null,
            rule.Note,
            rule.Blocks);
    }
}

/// <summary>Starts choosing a design for a service type of the currently published version.</summary>
/// <param name="ServiceTypeId">The service type, as <c>/current</c> named it.</param>
public sealed record StartDesignSelectionDraftRequest(Guid ServiceTypeId);

/// <summary>One group's answer, as a caller reads or writes a draft.</summary>
/// <param name="GroupCode">The group.</param>
/// <param name="OptionCodes">The options chosen.</param>
public sealed record DesignDraftSelectionPayload(string GroupCode, IReadOnlyList<string> OptionCodes)
{
    /// <summary>Projects a stored selection.</summary>
    public static DesignDraftSelectionPayload From(DesignDraftSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return new DesignDraftSelectionPayload(selection.GroupCode, selection.OptionCodes);
    }

    /// <summary>The command-shaped input the domain validates.</summary>
    public DesignSelectionInput ToInput() => new(GroupCode ?? string.Empty, OptionCodes ?? []);
}

/// <summary>Replaces the whole selection set of a draft.</summary>
/// <param name="Selections">What was chosen, one entry per group.</param>
/// <param name="Instructions">Free-text craft instructions, or null.</param>
public sealed record SaveDesignSelectionsRequest(
    IReadOnlyList<DesignDraftSelectionPayload>? Selections,
    string? Instructions);

/// <summary>A design selection draft, with the migration prompt a republish may have left standing.</summary>
public sealed record DesignSelectionDraftPayload(
    Guid DesignSelectionDraftId,
    Guid BranchId,
    Guid CatalogVersionId,
    Guid ServiceTypeId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    string? Instructions,
    IReadOnlyList<DesignDraftSelectionPayload> Selections,
    DesignMigrationPromptPayload? MigrationPrompt)
{
    /// <summary>Renders a draft read.</summary>
    /// <param name="read">The read.</param>
    /// <returns>The payload.</returns>
    public static DesignSelectionDraftPayload From(DesignDraftRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        return From(read.Draft, read.MigrationPlan);
    }

    /// <summary>Renders a draft together with an optional migration plan.</summary>
    /// <param name="draft">The draft.</param>
    /// <param name="plan">The plan, or null when the draft is already current.</param>
    /// <returns>The payload.</returns>
    public static DesignSelectionDraftPayload From(DesignSelectionDraft draft, DesignSelectionMigrationPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new DesignSelectionDraftPayload(
            draft.Id,
            draft.BranchId,
            draft.CatalogVersionId,
            draft.ServiceTypeId,
            draft.StartedAt,
            draft.UpdatedAt,
            draft.ExpiresAt,
            draft.ConsumedAt,
            draft.Instructions,
            [.. draft.Selections.Select(DesignDraftSelectionPayload.From)],
            plan is null ? null : DesignMigrationPromptPayload.From(plan));
    }
}

/// <summary>What a republish left standing against a pinned draft.</summary>
/// <param name="ServiceTypeStillOffered">
/// False when the published catalogue no longer offers this service type at all — the draft may still be
/// finished on the pinned version, but it can never be migrated.
/// </param>
/// <param name="Changes">Every reason the prompt names.</param>
public sealed record DesignMigrationPromptPayload(
    bool ServiceTypeStillOffered,
    IReadOnlyList<DesignMigrationChangePayload> Changes)
{
    /// <summary>Projects a plan.</summary>
    public static DesignMigrationPromptPayload From(DesignSelectionMigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new DesignMigrationPromptPayload(
            plan.ServiceTypeStillOffered, [.. plan.Changes.Select(DesignMigrationChangePayload.From)]);
    }
}

/// <summary>One thing the republish changed.</summary>
/// <param name="Kind">A stable dotted code the client branches on.</param>
/// <param name="GroupCode">The group the change is about, when there is one.</param>
/// <param name="OptionCode">The option, when the change is about one.</param>
/// <param name="RuleIdentifier">The rule's <c>DR-nn</c>, when the change is a rule added.</param>
/// <param name="Message">What changed, in the shop's words.</param>
public sealed record DesignMigrationChangePayload(
    string Kind,
    string? GroupCode,
    string? OptionCode,
    string? RuleIdentifier,
    string Message)
{
    /// <summary>Projects a change.</summary>
    public static DesignMigrationChangePayload From(DesignMigrationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new DesignMigrationChangePayload(
            change.Kind, change.GroupCode, change.OptionCode, change.RuleIdentifier, change.Message);
    }
}

/// <summary>What stands between a draft and confirmation.</summary>
/// <param name="DesignSelectionDraftId">The draft.</param>
/// <param name="Confirmable">Whether nothing blocking stands. A note never blocks.</param>
/// <param name="Violations">Every violation, blocking or not, in rule order.</param>
/// <param name="AutoSelections">What a <c>requires</c> rule selected on the customer's behalf.</param>
/// <param name="Notes">The standing instructions the selections attached.</param>
public sealed record DesignCheckPayload(
    Guid DesignSelectionDraftId,
    bool Confirmable,
    IReadOnlyList<DesignViolationPayload> Violations,
    IReadOnlyList<DesignAutoSelectionPayload> AutoSelections,
    IReadOnlyList<DesignNotePayload> Notes)
{
    /// <summary>Renders an evaluation.</summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="evaluation">The evaluation.</param>
    /// <returns>The payload.</returns>
    public static DesignCheckPayload From(Guid draftId, DesignEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        return new DesignCheckPayload(
            draftId,
            evaluation.IsConfirmable,
            [.. evaluation.Violations.Select(DesignViolationPayload.From)],
            [.. evaluation.AutoSelections.Select(DesignAutoSelectionPayload.From)],
            [.. evaluation.Notes.Select(DesignNotePayload.From)]);
    }
}

/// <summary>One thing wrong with a selection set.</summary>
public sealed record DesignViolationPayload(
    string Code,
    string? RuleIdentifier,
    string? GroupCode,
    IReadOnlyList<string> OptionCodes,
    string? RelatedGroupCode,
    IReadOnlyList<string> RelatedOptionCodes,
    string Message,
    bool Blocks)
{
    /// <summary>Projects a violation.</summary>
    public static DesignViolationPayload From(DesignViolation violation)
    {
        ArgumentNullException.ThrowIfNull(violation);

        return new DesignViolationPayload(
            violation.Code,
            violation.RuleIdentifier,
            violation.GroupCode,
            violation.OptionCodes,
            violation.RelatedGroupCode,
            violation.RelatedOptionCodes,
            violation.Message,
            violation.Blocks);
    }
}

/// <summary>An option a rule selected on the customer's behalf.</summary>
public sealed record DesignAutoSelectionPayload(string RuleIdentifier, string GroupCode, string OptionCode)
{
    /// <summary>Projects an auto-selection.</summary>
    public static DesignAutoSelectionPayload From(DesignAutoSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return new DesignAutoSelectionPayload(selection.RuleIdentifier, selection.GroupCode, selection.OptionCode);
    }
}

/// <summary>A standing instruction a selection attached.</summary>
public sealed record DesignNotePayload(string RuleIdentifier, string Text)
{
    /// <summary>Projects a note.</summary>
    public static DesignNotePayload From(DesignNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return new DesignNotePayload(note.RuleIdentifier, note.Text);
    }
}

/// <summary>What a migration command did, with the fresh evaluation of the version just migrated to.</summary>
/// <param name="Draft">The draft, re-pinned.</param>
/// <param name="AppliedChanges">Every change the migration prompt named, now applied.</param>
/// <param name="Evaluation">The rules, asked again.</param>
public sealed record DesignMigrationOutcomePayload(
    DesignSelectionDraftPayload Draft,
    IReadOnlyList<DesignMigrationChangePayload> AppliedChanges,
    DesignCheckPayload Evaluation)
{
    /// <summary>Renders a migration outcome.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The payload.</returns>
    public static DesignMigrationOutcomePayload From(DesignMigrationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new DesignMigrationOutcomePayload(
            DesignSelectionDraftPayload.From(outcome.Draft, null),
            [.. outcome.AppliedChanges.Select(DesignMigrationChangePayload.From)],
            DesignCheckPayload.From(outcome.Draft.Id, outcome.Evaluation));
    }
}
