using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Application.Preferences;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// What this module contributes to a customer's timeline.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It reads the audit trail, not this module's tables, and that is the whole design.</strong>
/// The tables hold the record as it stands; the timeline is a question about what happened to it, and
/// for most of what happened the tables have no answer. A record deactivated and then reactivated has
/// <c>deactivated_at</c> back to null and <c>updated_at</c> holding only the second change, so neither
/// event is recoverable from them. A correction that changed a telephone number writes no row anywhere
/// — only a name change records an alias. A communication preference is one row edited in place, so a
/// customer who has changed her mind four times leaves one <c>updated_at</c>. Building a timeline from
/// the tables would therefore not be a harder version of this; it would be a different and untrue
/// answer.
/// </para>
/// <para>
/// The trail, by contrast, has an entry for each of the thirteen things this module does, every one of
/// them written against the customer — <c>CustomerAudit</c>, <c>ConsentAudit</c> and
/// <c>PreferenceAudit</c> all use one entity type, and the export entries are deliberately recorded
/// against the person rather than the export, "because the question it answers later is 'who has seen
/// this person's data', and that is asked of the person". It is append-only in the database and
/// hash-chained. <c>IAuditReader</c>'s own remark settles it: "There is no per-module history table and
/// there should not be … a second copy would be a second thing that can disagree with the first."
/// </para>
/// <para>
/// One honest limitation follows and is worth knowing before somebody debugs it. The audit write is a
/// second transaction after the module's own — the handlers say "save first, then record: the house
/// order" — so a crash between the two commits the change and loses its entry, and this timeline would
/// then not show it. That gap belongs to the audit trail rather than to this class, it is the same gap
/// the administration screens read through, and it is what #77 fixed for the outbox and did not fix
/// here.
/// </para>
/// </remarks>
/// <param name="context">This module's context, used only to confirm the customer is the caller's.</param>
/// <param name="audit">The trail.</param>
public sealed class CustomerTimelineSource(CustomersDbContext context, IAuditReader audit) : ITimelineSource
{
    /// <summary>The name this module's entries are attributed to.</summary>
    public const string Name = "customers";

    /// <inheritdoc />
    public string SourceName => Name;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimelineEntry>> ReadAsync(
        TimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The trail has no organisation column — tenancy is established before the query, never inside
        // it — so this module confirms the customer is one of the caller's before reading a single
        // entry. The endpoint checks the same thing to decide between a timeline and a 404; this is not
        // that check repeated for its own sake, it is what makes the source safe to call from anywhere.
        var isOurs = await context.Customers
            .AsNoTracking()
            .AnyAsync(
                customer => customer.Id == query.CustomerId
                            && customer.OrganisationId == query.Organisation.OrganisationId,
                cancellationToken);

        if (!isOurs)
        {
            return [];
        }

        var visible = TimelineActions.VisibleTo(query.Permissions);

        if (visible.Count == 0)
        {
            return [];
        }

        // Over-fetch. The trail carries entries this caller may not be shown — a consent decision, an
        // export — and they are filtered here rather than in SQL, so a page filtered down to nothing
        // would end the timeline early if the read were bounded to the page size. Asking for the
        // maximum and keeping the caller's share is the simple version of that, and the ceiling on a
        // single customer's trail is small enough that it is also the cheap one.
        var trail = await audit.ReadEntityTrailAsync(
            new AuditTrailQuery(
                TimelineActions.EntityType,
                query.CustomerId,
                query.Before is { } before
                    ? new AuditTrailPosition(before.OccurredAt, before.EntryId)
                    : null,
                AuditQuery.MaximumLimit),
            cancellationToken);

        var mayReadReasons = query.Permissions.Contains(TimelineActions.ReasonPermission);

        return
        [
            .. trail
                .Where(entry => visible.Contains(entry.Action))
                .Take(query.Limit)
                .Select(entry => ToEntry(entry, mayReadReasons)),
        ];
    }

    private static TimelineEntry ToEntry(AuditTrailEntry entry, bool mayReadReasons)
    {
        var hasReason = !string.IsNullOrWhiteSpace(entry.Reason);

        return new TimelineEntry(
            entry.Id,
            entry.OccurredAt,
            Name,

            // The action is already a stable dotted name that the module owns and the matrix documents,
            // so it is the kind. Inventing a second vocabulary beside it would be two names for one
            // thing, and the screen would eventually be shown the wrong one.
            entry.Action,
            TimelineActions.TitleOf(entry.Action),
            entry.Summary,
            hasReason && mayReadReasons ? entry.Reason : null,
            hasReason ? TimelineActions.ReasonPermission : null,

            // Everything this module records is about the customer, so there is nothing else to open:
            // the entry's own reference is the record the timeline is already showing. Orders, Billing
            // and Custody are the sources that will carry a reference worth following.
            ReferenceType: null,
            ReferenceId: null,
            ExpandPermission: null,
            entry.BranchId,
            entry.ActorDisplayName);
    }
}
