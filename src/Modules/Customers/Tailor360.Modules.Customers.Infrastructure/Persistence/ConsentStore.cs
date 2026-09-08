using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// The consent register and record, over the <c>customers</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// The register comes back with its wordings loaded, because <c>CurrentWordingVersion</c> is computed
/// from them and a purpose read without them would report zero — which is the refusal that says a
/// purpose cannot be consented to yet. Getting that wrong would make every recording fail with a
/// message about unpublished wording that was published.
/// </para>
/// <para>
/// <strong>There is no update and no delete here, and the database agrees.</strong>
/// <c>customers.consent_records</c> carries a trigger that refuses both, so a mutation added to this
/// class would fail at the statement rather than silently rewrite evidence.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class ConsentStore(CustomersDbContext context) : IConsentStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsentPurpose>> ReadRegisterAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.ConsentPurposes
            .AsNoTracking()
            .Include(purpose => purpose.Wordings)
            .Where(purpose => purpose.OrganisationId == organisationId)
            .OrderBy(purpose => purpose.Key)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ConsentPurpose?> FindPurposeAsync(
        Guid organisationId,
        string purposeKey,
        CancellationToken cancellationToken = default)
        => await context.ConsentPurposes
            .AsNoTracking()
            .Include(purpose => purpose.Wordings)
            .FirstOrDefaultAsync(
                purpose => purpose.OrganisationId == organisationId && purpose.Key == purposeKey,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsentRecord>> ReadRecordsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await context.ConsentRecords
            .AsNoTracking()
            .Where(record => record.CustomerId == customerId)
            .OrderByDescending(record => record.RecordedAt)
            .ThenByDescending(record => record.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(ConsentRecord record) => context.ConsentRecords.Add(record);

    /// <inheritdoc />
    /// <remarks>
    /// No concurrency translation, unlike the customer record and the preference. Appending a row
    /// races with nothing: two answers taken at once are two answers, and the query that reads the
    /// latest breaks the tie by identifier.
    /// </remarks>
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
