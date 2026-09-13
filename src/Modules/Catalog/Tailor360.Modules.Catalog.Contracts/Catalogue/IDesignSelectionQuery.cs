using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Contracts.Catalogue;

/// <summary>
/// A draft's validated selections and its snapshot, by draft identity — the one way Orders (#32a) reads
/// a design selection draft at order confirmation. Orders never reads a catalogue table (#30, issue #140).
/// </summary>
/// <remarks>
/// <para>
/// A read contract, and deliberately not the thing that consumes the draft: this answers the same way
/// whether it is asked once or ten times, so a confirmation attempt that fails after asking it has asked
/// nothing it needs to undo. Spending the draft — recording that a confirmation used it — is a separate
/// capability the module publishes on its own aggregate, for whichever mechanism the confirmation ends
/// up using to reach it.
/// </para>
/// <para>
/// The rules are re-run here, against the draft's <em>pinned</em> version, exactly as
/// <see cref="IDesignSelectionValidator"/> is asked everywhere else — a picker's own copy of the rules is
/// never trusted at confirmation, and neither is whatever the draft looked like the last time somebody
/// saved it. <see cref="DesignSelectionSnapshot.IsConfirmable"/> is that answer, so a caller does not have
/// to run the validator a second time to learn what this method already asked it.
/// </para>
/// </remarks>
public interface IDesignSelectionQuery
{
    /// <summary>Reads one draft's current selections, freshly validated, and the snapshot they would freeze to.</summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="organisationId">The caller's organisation. A draft of another's is not found.</param>
    /// <param name="hasReferenceImage">
    /// Whether the garment holds a reference image, for a <c>requires attachment</c> rule. Catalog has no
    /// notion of a garment or its media, so the caller — Orders, at confirmation — supplies the answer;
    /// the picker's own <c>…/check</c> takes it the same way.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The snapshot, or the reason it could not be read.</returns>
    Task<Result<DesignSelectionSnapshot>> GetAsync(
        Guid draftId,
        Guid organisationId,
        bool hasReferenceImage,
        CancellationToken cancellationToken = default);
}

/// <summary>One design selection draft, as Orders reads it at confirmation.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="BranchId">The branch it was chosen at.</param>
/// <param name="IsOpen">Whether the draft may still be consumed — false once spent.</param>
/// <param name="IsConfirmable">
/// Whether nothing blocking stands, from the same rule evaluation <c>…/check</c> answers. A note never
/// blocks; the caller decides what to do with one that does.
/// </param>
/// <param name="Violations">Every violation the rules found, blocking or not, in rule order.</param>
/// <param name="Snapshot">The copy that would be frozen onto the garment job, built from the pinned version.</param>
public sealed record DesignSelectionSnapshot(
    Guid DraftId,
    Guid BranchId,
    bool IsOpen,
    bool IsConfirmable,
    IReadOnlyList<DesignViolation> Violations,
    GarmentDesignSnapshot Snapshot);
