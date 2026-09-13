using Tailor360.Modules.Catalog.Domain.Design;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Application.Abstractions;

/// <summary>
/// Reads and writes design selection drafts (#30, issue #140).
/// </summary>
/// <remarks>
/// No "one open draft" lookup, unlike <c>IMeasurementCaptureStore</c>: a measurement draft is found again
/// by branch, customer and template because it is reused across visits to the same customer, but a
/// design selection draft belongs to one garment section of one order draft
/// (<c>OrderDraftGarment.DesignSelectionDraftId</c>) and is only ever addressed by the identity that
/// command returned when it started one.
/// </remarks>
public interface IDesignSelectionDraftStore
{
    /// <summary>One draft, by identity.</summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="organisationId">The caller's organisation. A draft of another's is not found.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or null.</returns>
    Task<DesignSelectionDraft?> FindAsync(
        Guid draftId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a newly started draft to the context.</summary>
    /// <param name="draft">The draft.</param>
    void Add(DesignSelectionDraft draft);

    /// <summary>The draft's concurrency token, as the <c>ETag</c> a client sends back.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>The tag.</returns>
    EntityTag EntityTagOf(DesignSelectionDraft draft);

    /// <summary>Commits everything left in the context.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the conflict a concurrent writer caused.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
