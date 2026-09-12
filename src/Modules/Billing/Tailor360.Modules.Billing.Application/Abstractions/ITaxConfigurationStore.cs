using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>
/// Reads and writes tax configuration versions.
/// </summary>
/// <remarks>
/// A version is loaded whole — its codes and their components — because the version is the aggregate
/// and every publish rule spans it. Saving is separate from changing, as in every module: a method
/// that changes something leaves the change in the context and the caller decides when to commit.
/// </remarks>
public interface ITaxConfigurationStore
{
    /// <summary>One version and its codes. A version of another organisation is not found.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or null.</returns>
    Task<TaxConfigurationVersion?> FindAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>The organisation's published version, loaded whole.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published version, or null when none has been published.</returns>
    Task<TaxConfigurationVersion?> FindPublishedAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>The organisation's versions, newest first, without their codes.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The versions.</returns>
    Task<IReadOnlyList<TaxConfigurationVersion>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>One more than the highest version number in use, or one.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next number.</returns>
    Task<int> NextVersionNumberAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What every tax code concept has been spelled across every published version — the input to
    /// the rule that a published code never changes and is never re-used for another concept.
    /// </summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ledger.</returns>
    Task<TaxCodeLedger> ReadCodeHistoryAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new version to the context.</summary>
    /// <param name="version">The version.</param>
    void Add(TaxConfigurationVersion version);

    /// <summary>The version's concurrency token, as the <c>ETag</c> a client sends back.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The tag.</returns>
    EntityTag EntityTagOf(TaxConfigurationVersion version);

    /// <summary>Commits everything left in the context, translating a lost write race into a precondition failure.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the failure.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits a newly started draft, translating a version-number race into a conflict.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the conflict.</returns>
    Task<Result> SaveDraftAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits a publication, translating the one-published-version clash into a conflict.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the conflict.</returns>
    Task<Result> SavePublicationAsync(CancellationToken cancellationToken = default);
}

/// <summary>What tax codes have been spelled, across every version an organisation has published.</summary>
/// <param name="CodeByKey">The code each concept was last published under.</param>
/// <param name="KeyByCode">The concept each code has been published against.</param>
public sealed record TaxCodeLedger(
    IReadOnlyDictionary<Guid, string> CodeByKey,
    IReadOnlyDictionary<string, Guid> KeyByCode)
{
    /// <summary>A ledger holding nothing, for an organisation that has published no version yet.</summary>
    public static TaxCodeLedger Empty { get; } = new(
        new Dictionary<Guid, string>(),
        new Dictionary<string, Guid>(StringComparer.Ordinal));
}
