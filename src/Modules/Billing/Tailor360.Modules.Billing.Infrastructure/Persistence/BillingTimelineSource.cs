using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Billing.Application.Invoicing;
using Tailor360.Modules.Billing.Domain.Invoicing;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Billing.Infrastructure.Persistence;

/// <summary>
/// What this module contributes to a customer's timeline (#309): every posted invoice, its cancellation
/// and its credit and debit notes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It reads this module's own tables, not the audit trail, and that is the whole design.</strong>
/// <c>IAuditReader.ReadEntityTrailAsync</c> is keyed on one entity, and every Billing entry
/// <c>InvoiceHandler</c> writes is recorded against the <em>invoice</em> — <c>BillingAudit.InvoiceEntity</c>
/// — never against the customer. There is therefore no single entity whose trail is one customer's
/// documents, unlike Customers, whose <c>CustomerTimelineSource</c> reads the trail because every entry it
/// writes names the customer. The tables, by contrast, hold the fact and its instant directly, and a
/// posted invoice, its cancellation and its notes are immutable by database trigger, so the table is the
/// authority here.
/// </para>
/// <para>
/// Over-fetch, then filter and page in memory — the same shape <c>CustomerTimelineSource</c> uses over
/// the trail. A customer's posted invoices are read on <c>ix_invoices_organisation_customer_posted_at</c>,
/// bounded rather than paged at the database: every invoice contributes at least its own posted entry, so
/// asking for the largest page a caller may request already covers the largest page this source could be
/// asked to answer, before a cancellation or a note adds to what one invoice contributes.
/// </para>
/// </remarks>
/// <param name="context">This module's context.</param>
/// <param name="users">Resolves the acting member of staff's display name, once per page.</param>
public sealed class BillingTimelineSource(BillingDbContext context, IUserDirectory users) : ITimelineSource
{
    private const int MaximumInvoicesFetched = TimelineQuery.MaximumLimit;

    /// <inheritdoc />
    public string SourceName => BillingTimelineActions.SourceName;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimelineEntry>> ReadAsync(
        TimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var visible = BillingTimelineActions.VisibleTo(query.Permissions);
        if (visible.Count == 0 || query.AssignedBranches.Count == 0)
        {
            return [];
        }

        var invoices = await context.Invoices
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Include(invoice => invoice.Cancellation)
            .Include(invoice => invoice.Notes)
            .Where(invoice => invoice.OrganisationId == query.Organisation.OrganisationId
                            && invoice.CustomerId == query.CustomerId
                            && invoice.PostedAt != null)
            .OrderByDescending(invoice => invoice.PostedAt)
            .Take(MaximumInvoicesFetched)
            .ToListAsync(cancellationToken);

        var mayReadReasons = query.Permissions.Contains(BillingTimelineActions.ReasonPermission);

        var candidates = invoices
            .Where(invoice => query.AssignedBranches.Contains(invoice.BranchId))
            .SelectMany(invoice => RawEntriesOf(invoice).Select(raw => (Invoice: invoice, Raw: raw)))
            .Where(candidate => visible.Contains(candidate.Raw.Kind))
            .Where(candidate => query.Before is not { } before || IsStrictlyBefore(candidate.Raw.OccurredAt, candidate.Raw.Id, before))
            .OrderByDescending(candidate => candidate.Raw.OccurredAt)
            .ThenByDescending(candidate => candidate.Raw.Id)
            .Take(query.Limit)
            .ToList();

        // Once per page, never once per row: the obligation ITimelineSource's own remarks put on every
        // implementation, and the reason CustomerTimelineSource is not the pattern followed for this call.
        var actorIds = candidates
            .Where(candidate => candidate.Raw.ActorId is not null)
            .Select(candidate => candidate.Raw.ActorId!.Value)
            .Distinct()
            .ToArray();
        var actorNames = actorIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await users.FindManyAsync(actorIds, cancellationToken)).ToDictionary(actor => actor.UserId, actor => actor.DisplayName);

        return [.. candidates.Select(candidate => ToEntry(candidate.Invoice, candidate.Raw, mayReadReasons, actorNames))];
    }

    private static TimelineEntry ToEntry(Invoice invoice, RawEntry raw, bool mayReadReasons, Dictionary<Guid, string> actorNames)
    {
        var hasReason = !string.IsNullOrWhiteSpace(raw.Reason);
        var actorName = raw.ActorId is { } actorId && actorNames.TryGetValue(actorId, out var name) ? name : null;

        return new TimelineEntry(
            raw.Id,
            raw.OccurredAt,
            BillingTimelineActions.SourceName,
            raw.Kind,
            BillingTimelineActions.TitleOf(raw.Kind),
            raw.Detail,
            hasReason && mayReadReasons ? raw.Reason : null,
            hasReason ? BillingTimelineActions.ReasonPermission : null,
            ReferenceType: "Invoice",
            ReferenceId: invoice.Id,
            ExpandPermission: BillingPermissions.CreateInvoice,
            invoice.BranchId,
            actorName);
    }

    /// <summary>An invoice's own posted entry, its cancellation's and each of its notes', unordered.</summary>
    private static IEnumerable<RawEntry> RawEntriesOf(Invoice invoice)
    {
        if (invoice.PostedAt is { } postedAt)
        {
            yield return new RawEntry(
                invoice.Id, postedAt, InvoiceHandler.PostedAction,
                $"Invoice {invoice.InvoiceNumber} posted.", Reason: null, invoice.PostedBy);
        }

        if (invoice.Cancellation is { } cancellation)
        {
            yield return new RawEntry(
                cancellation.Id, cancellation.CancelledAt, InvoiceHandler.CancelledAction,
                $"Invoice {invoice.InvoiceNumber} cancelled.", cancellation.Reason, cancellation.CancelledBy);
        }

        foreach (var note in invoice.Notes)
        {
            var isCredit = note.Kind == AdjustmentNoteKind.Credit;
            yield return new RawEntry(
                note.Id, note.PostedAt, isCredit ? InvoiceHandler.CreditNotePostedAction : InvoiceHandler.DebitNotePostedAction,
                $"{(isCredit ? "Credit" : "Debit")} note {note.Number} posted against invoice {invoice.InvoiceNumber}.", note.Reason, note.PostedBy);
        }
    }

    /// <summary>The same total order and the same strict-inequality rule every other source applies.</summary>
    private static bool IsStrictlyBefore(DateTimeOffset occurredAt, Guid id, TimelinePosition before)
        => occurredAt < before.OccurredAt || (occurredAt == before.OccurredAt && id.CompareTo(before.EntryId) < 0);

    private readonly record struct RawEntry(Guid Id, DateTimeOffset OccurredAt, string Kind, string Detail, string? Reason, Guid? ActorId);
}
