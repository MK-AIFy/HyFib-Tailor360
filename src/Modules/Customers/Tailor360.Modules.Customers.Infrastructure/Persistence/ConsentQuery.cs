using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Modules.Customers.Domain.Consent;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// The published consent query, over the <c>customers</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The standing answer is the most recent record, computed on every read.</strong> There is no
/// "is current" flag to maintain and therefore none to fall out of step: withdrawing appends a
/// <see cref="ConsentDecision.Withdrawn"/> row and agreeing again appends another
/// <see cref="ConsentDecision.Granted"/> one, and this query reads the last of them. The index
/// <c>ix_consent_records_customer_purpose_recorded</c> is ordered to make that read a single seek.
/// </para>
/// <para>
/// <strong>The tie-break is the identifier, and it is a tie-break rather than a judgement.</strong>
/// Two records for the same purpose could in principle share a <c>RecordedAt</c>. Ordering by
/// identifier after time makes the answer <em>total and repeatable</em> — the same row on every read,
/// and the same row the counter screen shows, instead of whatever order the database happened to
/// return. It does not claim to say which of two simultaneous answers was given second: a version-7
/// identifier orders by time only down to the millisecond, and two generated inside one differ only in
/// random bits. Nothing in the row records a finer order, so this query does not pretend one exists.
/// </para>
/// <para>
/// <strong>The order is decided here, in SQL, and nowhere else.</strong> The store reads a customer's
/// records with the same expression and the handler that serves the screen keeps that order rather
/// than re-sorting in memory — which matters, because PostgreSQL orders a <c>uuid</c> by its bytes
/// while .NET's <c>Guid</c> comparison walks its fields in a different order, so two sorts of the same
/// rows would not always agree. One authority, and the screen and the contract cannot disagree.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class ConsentQuery(CustomersDbContext context) : IConsentQuery
{
    /// <inheritdoc />
    public async Task<ConsentState> GetAsync(
        Guid customerId,
        string purposeKey,
        CancellationToken cancellationToken = default)
    {
        var key = purposeKey?.Trim() ?? string.Empty;

        if (key.Length == 0)
        {
            return ConsentState.NeverAsked(customerId, key);
        }

        // The answer is the surviving record's, and it is reported under the surviving record's
        // identifier, so a consumer that de-duplicates on it cannot end up holding two answers for
        // one person.
        var subject = await SurvivorOfAsync(customerId, cancellationToken);

        var latest = await context.ConsentRecords
            .AsNoTracking()
            .Where(record => record.CustomerId == subject && record.PurposeKey == key)
            .OrderByDescending(record => record.RecordedAt)
            .ThenByDescending(record => record.Id)
            .Select(record => new
            {
                record.Id,
                record.Decision,
                record.WordingVersion,
                record.RecordedAt,
                record.Source,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return latest is null
            ? ConsentState.NeverAsked(subject, key)
            : new ConsentState(
                subject,
                key,
                StatusOf(latest.Decision),
                latest.WordingVersion,
                latest.RecordedAt,
                latest.Source,
                latest.Id);
    }

    /// <summary>
    /// Maps the module's decision onto the published status.
    /// </summary>
    /// <remarks>
    /// Written out rather than cast, so that the two enumerations are free to be numbered differently —
    /// and they are, because the published one reserves zero for
    /// <see cref="ConsentStatus.NeverAsked"/>. A decision that matched no arm would be one the domain
    /// refused to construct and the database's check constraint refused to store, so the discard is
    /// unreachable; it answers <see cref="ConsentStatus.NeverAsked"/> anyway, because a value nobody
    /// can interpret must not be the one that permits something.
    /// </remarks>
    /// <summary>
    /// The record that answers for this customer today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A caller can be holding an identifier from before a merge — a queued notification, an order
    /// taken last year — and <c>docs/prd/exceptions.md</c> EX-01 is explicit that after a merge "the
    /// survivor's consent and channel preferences govern every later message". Reading the folded
    /// record's answer would honour a grant the person has since withdrawn on the record that
    /// survived, which is the one failure this whole area exists to prevent, and it would do so
    /// before any merge handler had a chance to re-point anything.
    /// </para>
    /// <para>
    /// One hop is always enough. A merge flattens every pointer that named the record it is folding
    /// in, so a stored pointer always names a record that stands.
    /// </para>
    /// </remarks>
    /// <param name="customerId">The identifier the caller supplied.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The surviving record, or the identifier as supplied when it stands on its own.</returns>
    private async Task<Guid> SurvivorOfAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var mergedInto = await context.Customers
            .AsNoTracking()
            .Where(customer => customer.Id == customerId)
            .Select(customer => customer.MergedIntoCustomerId)
            .FirstOrDefaultAsync(cancellationToken);

        // Null for a record that stands and for one that does not exist, and the caller's own
        // identifier is the right answer to both.
        return mergedInto ?? customerId;
    }

    private static ConsentStatus StatusOf(ConsentDecision decision) => decision switch
    {
        ConsentDecision.Granted => ConsentStatus.Granted,
        ConsentDecision.Declined => ConsentStatus.Declined,
        ConsentDecision.Withdrawn => ConsentStatus.Withdrawn,
        _ => ConsentStatus.NeverAsked,
    };
}
