using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Contributes entries to the customer timeline. Each module implements this for the facts it owns and
/// the web host composes the results, so no module reads another module's tables to build the view.
/// </summary>
/// <remarks>
/// <para>
/// This is the inverted port: every other member of this folder is infrastructure a module <em>calls</em>,
/// and this is one a module <em>implements</em> for the host to call. That inversion is what
/// <c>docs/architecture/architecture-rules.md</c> settles as ROD-02 — the port is declared here, each
/// module implements it in its own <c>Infrastructure</c>, and the host merges. A cross-module join
/// would need an architecture decision record; a merge does not.
/// </para>
/// <para>
/// A source is asked for one page at a time and answers only about what it owns. It never sees another
/// source's entries, never decides the order they end up in, and is never told how many the caller will
/// finally be shown — the host asks each source for a page, merges them, and keeps the newest.
/// </para>
/// </remarks>
public interface ITimelineSource
{
    /// <summary>
    /// A stable name for the contributing module, used to attribute an entry and to report a source
    /// that failed without failing the whole timeline.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// This source's entries for one customer, newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two obligations, and neither is the host's to enforce afterwards. An implementation returns only
    /// entries the caller is entitled to see — <see cref="TimelineQuery.Permissions"/> is the whole
    /// input to that decision, never a role — and it returns at most
    /// <see cref="TimelineQuery.Limit"/> of them, newest first, strictly older than
    /// <see cref="TimelineQuery.Before"/> when one is given.
    /// </para>
    /// <para>
    /// An implementation with nothing to contribute returns an empty list rather than throwing. The
    /// host treats a throwing source as a partial failure and says so in the response; it does not fail
    /// the request, because a customer's history is still worth reading when one module is unwell.
    /// </para>
    /// </remarks>
    /// <param name="query">Which customer, for whom, and where to resume.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>At most <see cref="TimelineQuery.Limit"/> entries, newest first.</returns>
    Task<IReadOnlyList<TimelineEntry>> ReadAsync(
        TimelineQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>What one source is being asked for.</summary>
/// <param name="CustomerId">The customer whose history is being read.</param>
/// <param name="Organisation">
/// The organisation the request belongs to. A source that stores an organisation identifier filters on
/// it; one that reaches the customer through a record it has already scoped does not need to repeat it.
/// </param>
/// <param name="AssignedBranches">
/// The branches the caller may act in. A customer record is organisation-wide, so this does not decide
/// whether the timeline may be read at all — the endpoint has already decided that — but it is what a
/// source consults when an entry of its own is branch-owned.
/// </param>
/// <param name="Permissions">
/// The caller's effective permissions. A source filters its entries on these, never on a role, for the
/// same reason the field masks do: an administrator who invents a custom role gets the right answer
/// without anybody adding a case.
/// </param>
/// <param name="Before">
/// Return only entries strictly older than this position, or null for the newest page.
/// </param>
/// <param name="Limit">The most entries to return.</param>
public sealed record TimelineQuery(
    Guid CustomerId,
    OrganisationContext Organisation,
    IReadOnlySet<Guid> AssignedBranches,
    IReadOnlySet<string> Permissions,
    TimelinePosition? Before,
    int Limit)
{
    /// <summary>The page size when the caller does not choose one.</summary>
    public const int DefaultLimit = 25;

    /// <summary>The largest page the timeline will return.</summary>
    public const int MaximumLimit = 100;
}

/// <summary>
/// A position in the merged timeline: the instant of an entry and its identity.
/// </summary>
/// <remarks>
/// <para>
/// The instant alone is not a position. Two things can happen to a customer inside one clock tick — a
/// merge writes an entry against both records at the same instant, by construction — and a page that
/// resumed on the instant alone would either repeat that pair or skip it. Adding the identifier makes
/// the pair a <em>total</em> order, and the same one every source and the database agree on, which is
/// exactly what paging needs: no entry is returned twice and none is stepped over.
/// </para>
/// <para>
/// What it is not is a promise about causality inside one instant. <c>Guid.CreateVersion7</c> times its
/// prefix to the millisecond and fills the rest with random data, so two identifiers minted in the same
/// millisecond are ordered by that random part rather than by which was minted first. Two entries
/// sharing an instant may therefore appear in either order — consistently, on every read, but not
/// necessarily in the order they happened. The window is narrower than it sounds, because the instant
/// is stored to the microsecond and two requests are milliseconds apart; the case that reaches it is
/// the one where a single unit of work writes two entries, and there neither order is more true than
/// the other.
/// </para>
/// </remarks>
/// <param name="OccurredAt">The instant of the entry the previous page stopped at.</param>
/// <param name="EntryId">Its identifier.</param>
public readonly record struct TimelinePosition(DateTimeOffset OccurredAt, Guid EntryId);

/// <summary>One entry on a customer's timeline.</summary>
/// <param name="Id">
/// The entry, which is the identifier of the fact it was built from and is half of a
/// <see cref="TimelinePosition"/>. Unique within its source; the source name makes it unique overall.
/// </param>
/// <param name="OccurredAt">When it happened, in UTC, by the server's clock.</param>
/// <param name="Source">The <see cref="ITimelineSource.SourceName"/> that contributed it.</param>
/// <param name="Kind">
/// Stable dotted kind, for example <c>customers.consent.recorded</c>, which is what a screen turns into
/// an icon and a label. It is part of the published contract: renaming one is an API change.
/// </param>
/// <param name="Title">Short human-readable title, already in the shop's words.</param>
/// <param name="Detail">
/// What happened, in the shop's words, written by the module that recorded it. Operational prose about
/// the record — never the customer's own data, and never anything a member of staff typed freehand.
/// </param>
/// <param name="Reason">
/// The reason the actor gave, where the action demanded one, or null. This is free text a member of
/// staff typed about a named person, which <c>docs/nfr/data-classification.md</c> classifies as
/// customer notes, so it is gated separately from the entry itself.
/// </param>
/// <param name="ReasonPermission">
/// The permission that shows <paramref name="Reason"/>. Set whenever the entry had a reason, whether
/// or not this caller was shown it, so a screen can say "somebody gave a reason you may not read"
/// rather than implying none was given. Null when the action never had one.
/// </param>
/// <param name="ReferenceType">The kind of thing the entry links to, for example <c>Order</c>.</param>
/// <param name="ReferenceId">Identity of the thing the entry links to.</param>
/// <param name="ExpandPermission">
/// The permission a caller must hold to open what <paramref name="ReferenceId"/> names, or null when
/// there is nothing to open. The entry is on the timeline either way: what it links to is a different
/// question from whether it happened.
/// </param>
/// <param name="BranchId">The branch the entry belongs to, where it belongs to one.</param>
/// <param name="ActorDisplayName">
/// Who did it, as their name was at the time, or null for the system. A member of staff's name, not the
/// customer's.
/// </param>
public sealed record TimelineEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Source,
    string Kind,
    string Title,
    string? Detail,
    string? Reason,
    string? ReasonPermission,
    string? ReferenceType,
    Guid? ReferenceId,
    string? ExpandPermission,
    Guid? BranchId,
    string? ActorDisplayName)
{
    /// <summary>This entry's position, for resuming a page after it.</summary>
    public TimelinePosition Position => new(OccurredAt, Id);
}
