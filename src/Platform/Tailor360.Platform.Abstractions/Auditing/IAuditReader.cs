namespace Tailor360.Platform.Abstractions.Auditing;

/// <summary>
/// Reads the audit trail: what happened to one thing, or what one person did.
/// </summary>
/// <remarks>
/// The trail is the before-and-after history the administration screens show. There is no per-module
/// history table and there should not be: the entries are already written in the same unit of work as
/// the changes they describe, already hash-chained, and already indexed by subject, actor and time. A
/// second copy would be a second thing that can disagree with the first.
/// </remarks>
public interface IAuditReader
{
    /// <summary>Reads one page of entries.</summary>
    /// <param name="query">What to look for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AuditPage> SearchAsync(AuditQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads what happened to one thing, newest first, keyed on when rather than on where in the chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SearchAsync"/> pages on the chain sequence, which is the right key for an
    /// investigation: it is monotonic, unique and needs no tie-break. It is the wrong key for anything
    /// a customer-facing screen pages through, because the sequence counts every write the installation
    /// has ever made, and
    /// <c>docs/architecture/conventions.md</c> section 3.1 says a sequential identifier is never
    /// exposed. A cursor built from it would leak that count to anybody who decoded one.
    /// </para>
    /// <para>
    /// So this reads the same rows on a different key — <c>(occurredAt, id)</c>, both of which are
    /// already public on every entry, with the identifier a UUIDv7 whose time-ordered prefix makes the
    /// pair a total order. It exists for the customer timeline of #26, which composes one page across
    /// several modules and needs a position each of them can resume from.
    /// </para>
    /// </remarks>
    /// <param name="query">Which entity, and where to resume.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>At most <see cref="AuditTrailQuery.Limit"/> entries, newest first.</returns>
    Task<IReadOnlyList<AuditTrailEntry>> ReadEntityTrailAsync(
        AuditTrailQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>One entity's trail, and where in it to resume.</summary>
/// <param name="EntityType">The kind of thing, for example <c>customers.customer</c>.</param>
/// <param name="EntityId">Which one.</param>
/// <param name="Before">
/// Return only entries strictly older than this position, or null for the newest page. "Older" is the
/// pair compared in order: an earlier instant, or the same instant and a lower identifier.
/// </param>
/// <param name="Limit">How many entries to return, clamped to <see cref="AuditQuery.MaximumLimit"/>.</param>
public sealed record AuditTrailQuery(
    string EntityType,
    Guid EntityId,
    AuditTrailPosition? Before = null,
    int Limit = AuditQuery.DefaultLimit);

/// <summary>A position in one entity's trail.</summary>
/// <param name="OccurredAt">The instant of the entry the page stopped at.</param>
/// <param name="EntryId">Its identifier, which breaks a tie between two entries at one instant.</param>
public readonly record struct AuditTrailPosition(DateTimeOffset OccurredAt, Guid EntryId);

/// <summary>One entry of an entity's trail.</summary>
/// <remarks>
/// Deliberately not <see cref="AuditRecord"/>. That carries the chain sequence, which is the one field
/// a customer-facing surface may not be handed, and it carries no identifier, which is the one field a
/// position needs.
/// </remarks>
/// <param name="Id">The entry, which is a UUIDv7 and is half of a position.</param>
/// <param name="OccurredAt">When it happened, in UTC.</param>
/// <param name="Action">The stable dotted action name.</param>
/// <param name="ActorId">Who acted, or null for the system.</param>
/// <param name="ActorDisplayName">Their name, as it was at the time.</param>
/// <param name="BranchId">The branch the action was taken in, where there was one.</param>
/// <param name="Reason">The reason the actor gave, where the action demanded one.</param>
/// <param name="Summary">What happened, in words.</param>
public sealed record AuditTrailEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    Guid? ActorId,
    string ActorDisplayName,
    Guid? BranchId,
    string? Reason,
    string Summary);

/// <summary>What an auditor is looking for.</summary>
/// <remarks>
/// Every filter narrows; none of them widens. An empty query reads the whole trail newest first, which
/// is what an investigation starts from, and each filter added answers a narrower question — what
/// happened to this account, what this person did, what happened that week.
/// </remarks>
/// <param name="EntityType">Only entries about this kind of thing, for example <c>StaffUser</c>.</param>
/// <param name="EntityId">Only entries about this one, which requires <paramref name="EntityType"/>.</param>
/// <param name="ActorId">Only entries recorded against this actor.</param>
/// <param name="ActionPrefix">Only actions starting with this, for example <c>identity.user.</c>.</param>
/// <param name="From">Only entries at or after this instant.</param>
/// <param name="To">Only entries strictly before this instant.</param>
/// <param name="Cursor">Where to continue from, or null for the first page.</param>
/// <param name="Limit">How many entries to return.</param>
public sealed record AuditQuery(
    string? EntityType = null,
    Guid? EntityId = null,
    Guid? ActorId = null,
    string? ActionPrefix = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Cursor = null,
    int Limit = AuditQuery.DefaultLimit)
{
    /// <summary>The page size when the caller does not choose one.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest page a read will return.</summary>
    public const int MaximumLimit = 200;

    /// <summary>The largest page an export will return in one request.</summary>
    /// <remarks>
    /// Higher than a screen's, because an export is a file somebody opens in a spreadsheet, and lower
    /// than unbounded, because the trail of a busy shop is millions of rows and a request that tried to
    /// return all of them would fail slowly rather than quickly.
    /// </remarks>
    public const int MaximumExportLimit = 1000;
}

/// <summary>One page of the trail, newest first, and where to continue from.</summary>
/// <param name="Entries">The entries on this page.</param>
/// <param name="NextCursor">
/// The cursor for the following page, or null when this is the last. Keyset on the sequence, which is
/// what makes paging an append-only table stable while it is still being appended to.
/// </param>
public sealed record AuditPage(IReadOnlyList<AuditRecord> Entries, string? NextCursor);

/// <summary>One entry as a reviewer reads it.</summary>
/// <param name="Sequence">Its position in the chain, which is what an operator quotes.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Action">The stable dotted action name.</param>
/// <param name="EntityType">The kind of thing it was about.</param>
/// <param name="EntityId">Which one.</param>
/// <param name="ActorId">Who acted, or null for the system.</param>
/// <param name="ActorDisplayName">Their name, as it was at the time.</param>
/// <param name="BranchId">The branch the action was taken in, where there was one.</param>
/// <param name="CorrelationId">The request this belonged to, for joining it to logs and traces.</param>
/// <param name="Reason">The reason the actor gave, where the action demanded one.</param>
/// <param name="Summary">What happened, in words.</param>
/// <param name="Before">Redacted prior state, or null for a creation.</param>
/// <param name="After">Redacted resulting state, or null for a deletion.</param>
public sealed record AuditRecord(
    long Sequence,
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    Guid EntityId,
    Guid? ActorId,
    string ActorDisplayName,
    Guid? BranchId,
    string? CorrelationId,
    string? Reason,
    string Summary,
    string? Before,
    string? After);
