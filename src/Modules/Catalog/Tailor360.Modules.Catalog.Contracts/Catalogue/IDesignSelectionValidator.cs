using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Contracts.Catalogue;

/// <summary>
/// Answers whether a set of design selections is one the catalogue allows (#30, issue #138).
/// </summary>
/// <remarks>
/// <para>
/// The one code path for the rules, at intake, at estimate, at confirmation and at a design revision
/// (<c>docs/prd/design-options.md</c> section 4). The engine behind it is pure and deterministic: it
/// holds no state, reads no clock of its own, and two calls with the same inputs give the same
/// answer in the same order. Everything that makes the answer decidable travels in the request — the
/// branch and the day, because whether an option is offerable depends on them, and whether the
/// garment holds a reference image, because one rule type reads the garment rather than the catalogue.
/// </para>
/// <para>
/// Rules are evaluated <strong>server-side and authoritatively</strong>. A picker may run its own copy
/// for the customer's benefit; what reaches confirmation is re-validated here regardless of what the
/// client believed.
/// </para>
/// </remarks>
public interface IDesignSelectionValidator
{
    /// <summary>Evaluates one garment's selections against the rules of its category.</summary>
    /// <param name="request">What was chosen, where, when, and for which garment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The evaluation, or the reason it could not be made — an unknown version or category.</returns>
    Task<Result<DesignEvaluation>> ValidateAsync(
        DesignSelectionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>One garment's design selections and the context that makes the rules decidable.</summary>
/// <param name="OrganisationId">The organisation whose catalogue it is.</param>
/// <param name="CatalogVersionId">The version the draft is pinned to, published or not.</param>
/// <param name="CategoryId">The garment's category in that version.</param>
/// <param name="BranchId">The branch the garment is ordered at, which decides what is offerable.</param>
/// <param name="EvaluatedOn">The day, in the branch's timezone, which decides the active periods.</param>
/// <param name="Selections">What was chosen, one entry per group; a group not listed is unset.</param>
/// <param name="HasReferenceImage">Whether the garment holds at least one reference-media object.</param>
public sealed record DesignSelectionRequest(
    Guid OrganisationId,
    Guid CatalogVersionId,
    Guid CategoryId,
    Guid BranchId,
    DateOnly EvaluatedOn,
    IReadOnlyList<DesignSelection> Selections,
    bool HasReferenceImage);

/// <summary>The options chosen in one group.</summary>
/// <param name="GroupCode">The group.</param>
/// <param name="OptionCodes">The option codes chosen — one for a single-choice group, any number otherwise.</param>
public sealed record DesignSelection(string GroupCode, IReadOnlyList<string> OptionCodes);

/// <summary>What the rules made of a selection set.</summary>
/// <param name="IsConfirmable">Whether nothing blocking stands. A note never blocks.</param>
/// <param name="Violations">Every violation, blocking or not, in rule order.</param>
/// <param name="AutoSelections">
/// The options a <c>requires</c> rule with exactly one admissible consequent selected on the
/// customer's behalf (section 4 rule 9). The caller applies them and says so in its summary.
/// </param>
/// <param name="Notes">The standing instructions the selections attached, for the snapshot and the job card.</param>
public sealed record DesignEvaluation(
    bool IsConfirmable,
    IReadOnlyList<DesignViolation> Violations,
    IReadOnlyList<DesignAutoSelection> AutoSelections,
    IReadOnlyList<DesignNote> Notes);

/// <summary>One thing wrong with a selection set.</summary>
/// <param name="Code">A stable dotted code the picker branches on, for example <c>design.excluded</c>.</param>
/// <param name="RuleIdentifier">The <c>DR-nn</c> the violation is of, or null when it is not a rule's.</param>
/// <param name="GroupCode">The group the violation is placed on.</param>
/// <param name="OptionCodes">The options on that side, when any.</param>
/// <param name="RelatedGroupCode">The other side's group, when a rule has two.</param>
/// <param name="RelatedOptionCodes">The other side's options.</param>
/// <param name="Message">What is wrong, in the shop's words: codes and rules, never anything about a customer.</param>
/// <param name="Blocks">Whether confirmation is refused while it stands.</param>
public sealed record DesignViolation(
    string Code,
    string? RuleIdentifier,
    string? GroupCode,
    IReadOnlyList<string> OptionCodes,
    string? RelatedGroupCode,
    IReadOnlyList<string> RelatedOptionCodes,
    string Message,
    bool Blocks);

/// <summary>An option a rule selected on the customer's behalf, because it was the only one it could.</summary>
/// <param name="RuleIdentifier">The rule.</param>
/// <param name="GroupCode">The group.</param>
/// <param name="OptionCode">The option.</param>
public sealed record DesignAutoSelection(string RuleIdentifier, string GroupCode, string OptionCode);

/// <summary>A standing instruction a selection attached to the garment.</summary>
/// <param name="RuleIdentifier">The rule.</param>
/// <param name="Text">The instruction.</param>
public sealed record DesignNote(string RuleIdentifier, string Text);
