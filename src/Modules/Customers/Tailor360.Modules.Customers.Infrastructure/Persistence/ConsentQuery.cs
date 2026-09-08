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
/// <strong>The tie-break is the identifier, and it is not arbitrary.</strong> Two records for the same
/// purpose can share a <c>RecordedAt</c> — the clock has a resolution, and a screen that records five
/// answers at once takes one instant for all of them. Identifiers are UUIDv7, which sort by the time
/// they were generated, so ordering by identifier after time puts the later of two simultaneous
/// answers last, which is the one that stands.
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

        var latest = await context.ConsentRecords
            .AsNoTracking()
            .Where(record => record.CustomerId == customerId && record.PurposeKey == key)
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
            ? ConsentState.NeverAsked(customerId, key)
            : new ConsentState(
                customerId,
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
    private static ConsentStatus StatusOf(ConsentDecision decision) => decision switch
    {
        ConsentDecision.Granted => ConsentStatus.Granted,
        ConsentDecision.Declined => ConsentStatus.Declined,
        ConsentDecision.Withdrawn => ConsentStatus.Withdrawn,
        _ => ConsentStatus.NeverAsked,
    };
}
