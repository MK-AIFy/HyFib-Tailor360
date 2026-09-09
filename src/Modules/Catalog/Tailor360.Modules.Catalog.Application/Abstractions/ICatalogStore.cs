using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Catalog.Application.Abstractions;

/// <summary>
/// Reads and writes catalogue versions.
/// </summary>
/// <remarks>
/// <para>
/// A version is loaded whole — its categories, its service types, their branches and their design
/// groups — because the version is the aggregate and every rule worth enforcing spans it. The data is
/// small by construction: the seeded catalogue is seven categories and eighteen service types, and a
/// shop that grew to ten times that would still fit in one round trip.
/// </para>
/// <para>
/// <strong>Saving is separate from changing.</strong> Every method here that changes something leaves
/// the change in the context and the caller decides when to commit, so a command that publishes a
/// version and writes an integration event commits both together or neither.
/// </para>
/// </remarks>
public interface ICatalogStore
{
    /// <summary>One version and everything in it.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="organisationId">The caller's organisation. A version of another's is not found.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or null.</returns>
    Task<CatalogVersion?> FindAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>The organisation's published version, loaded whole.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published version, or null when none has been published.</returns>
    Task<CatalogVersion?> FindPublishedAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>The organisation's versions, newest first, without their contents.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The versions.</returns>
    Task<IReadOnlyList<CatalogVersion>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>The next version number in the organisation's sequence.</summary>
    /// <remarks>
    /// Read rather than allocated from a sequence because it is a label an administrator reads, not an
    /// identifier: a gap left by a discarded draft would be confusing, and two drafts briefly claiming
    /// the same number is harmless — only one of them can ever be published.
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One more than the highest number in use, or one.</returns>
    Task<int> NextVersionNumberAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What every category and service type has been called across every published version.
    /// </summary>
    /// <remarks>
    /// The input to the two publish rules that are about the past: a code may not change once its
    /// record has been published, and a code may never be re-used for a different concept. Both need
    /// history rather than the current published version, because a concept retired two versions ago
    /// still owns its code.
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The category history and the service-type history.</returns>
    Task<CatalogCodeLedger> ReadCodeHistoryAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new version to the context.</summary>
    /// <param name="version">The version.</param>
    void Add(CatalogVersion version);

    /// <summary>The version's concurrency token, as the <c>ETag</c> a client sends back.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The tag.</returns>
    EntityTag EntityTagOf(CatalogVersion version);

    /// <summary>Commits everything left in the context.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows written.</returns>
    Task<int> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits a newly started draft.</summary>
    /// <remarks>
    /// The next version number is read and then written, so two administrators starting a draft in the
    /// same moment can both read the same maximum and both choose the same number. The unique index
    /// settles it, and the loser is answered a conflict rather than a five hundred: nothing was
    /// created, and asking again takes the number after. The translation lives here because this is
    /// the layer that knows the index exists.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the conflict.</returns>
    Task<Result> SaveDraftAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a publication, translating the one-published-version clash into a conflict.
    /// </summary>
    /// <remarks>
    /// A separate method rather than a flag, because it is the only save in the module whose failure is
    /// an ordinary outcome rather than a defect. "Exactly one published version is current at any time"
    /// is a partial unique index, and two administrators publishing different drafts in the same second
    /// is a race the database settles: one commits, and the other must be told plainly that somebody
    /// else's version is now live rather than being shown a five hundred. The translation lives here
    /// because this is the layer that knows the index exists.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the conflict.</returns>
    Task<Result> SavePublicationAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What codes have meant, across every version an organisation has published.
/// </summary>
/// <param name="CategoryCodeByKey">The code each category concept was last published under.</param>
/// <param name="CategoryKeyByCode">The category concept each code has been published against.</param>
/// <param name="ServiceCodeByKey">The qualified reference each service concept was last published under.</param>
/// <param name="ServiceKeyByCode">The service concept each qualified reference has been published against.</param>
public sealed record CatalogCodeLedger(
    IReadOnlyDictionary<Guid, string> CategoryCodeByKey,
    IReadOnlyDictionary<string, Guid> CategoryKeyByCode,
    IReadOnlyDictionary<Guid, string> ServiceCodeByKey,
    IReadOnlyDictionary<string, Guid> ServiceKeyByCode)
{
    /// <summary>A ledger holding nothing, for an organisation that has published no version yet.</summary>
    public static CatalogCodeLedger Empty { get; } = new(
        new Dictionary<Guid, string>(),
        new Dictionary<string, Guid>(StringComparer.Ordinal),
        new Dictionary<Guid, string>(),
        new Dictionary<string, Guid>(StringComparer.Ordinal));
}
