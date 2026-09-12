using Tailor360.Modules.Billing.Domain.Pricing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>Reads and writes price lists and their versions.</summary>
/// <remarks>
/// A version is loaded whole — its items, rules and branches — because the version is the aggregate.
/// Saving is separate from changing, as in every module.
/// </remarks>
public interface IPriceListStore
{
    /// <summary>One price list. A list of another organisation is not found.</summary>
    Task<PriceList?> FindListAsync(Guid priceListId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>One price list by code.</summary>
    Task<PriceList?> FindListByCodeAsync(string code, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>The organisation's price lists, by code.</summary>
    Task<IReadOnlyList<PriceList>> ListListsAsync(Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>Adds a price list to the context.</summary>
    void AddList(PriceList priceList);

    /// <summary>The list's concurrency token.</summary>
    EntityTag EntityTagOf(PriceList priceList);

    /// <summary>One version and everything in it. A version of another organisation is not found.</summary>
    Task<PriceListVersion?> FindVersionAsync(Guid versionId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>A list's published version, loaded whole, or null.</summary>
    Task<PriceListVersion?> FindPublishedAsync(Guid priceListId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>Every published version of the organisation, across its lists, loaded whole.</summary>
    Task<IReadOnlyList<PriceListVersion>> PublishedVersionsAsync(Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>A list's versions, newest first, without their contents.</summary>
    Task<IReadOnlyList<PriceListVersion>> ListVersionsAsync(Guid priceListId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>One more than the highest version number the list holds, or one.</summary>
    Task<int> NextVersionNumberAsync(Guid priceListId, CancellationToken cancellationToken = default);

    /// <summary>What every item and rule concept of a list has been spelled across its published versions.</summary>
    Task<PriceListLedger> ReadCodeHistoryAsync(Guid priceListId, CancellationToken cancellationToken = default);

    /// <summary>Adds a version to the context.</summary>
    void AddVersion(PriceListVersion version);

    /// <summary>The version's concurrency token.</summary>
    EntityTag EntityTagOf(PriceListVersion version);

    /// <summary>Commits everything left in the context, translating a lost write race into a precondition failure.</summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits a new list or draft, translating a code or number race into a conflict.</summary>
    Task<Result> SaveDraftAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits a publication, translating the one-published-version clash into a conflict.</summary>
    Task<Result> SavePublicationAsync(CancellationToken cancellationToken = default);
}

/// <summary>What item and rule codes have meant across every version a list has published.</summary>
public sealed record PriceListLedger(
    IReadOnlyDictionary<Guid, string> ItemCodeByKey,
    IReadOnlyDictionary<string, Guid> ItemKeyByCode,
    IReadOnlyDictionary<Guid, string> RuleCodeByKey,
    IReadOnlyDictionary<string, Guid> RuleKeyByCode)
{
    /// <summary>A ledger holding nothing.</summary>
    public static PriceListLedger Empty { get; } = new(
        new Dictionary<Guid, string>(),
        new Dictionary<string, Guid>(StringComparer.Ordinal),
        new Dictionary<Guid, string>(),
        new Dictionary<string, Guid>(StringComparer.Ordinal));
}
