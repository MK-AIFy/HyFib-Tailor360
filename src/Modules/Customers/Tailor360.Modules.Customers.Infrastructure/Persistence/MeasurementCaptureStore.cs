using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// Drafts and confirmed measurements over the Customers context.
/// </summary>
/// <param name="context">The module's context.</param>
public sealed class MeasurementCaptureStore(CustomersDbContext context) : IMeasurementCaptureStore
{
    /// <inheritdoc />
    public async Task<MeasurementDraft?> FindOpenDraftAsync(
        Guid branchId,
        Guid customerId,
        Guid templateId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.MeasurementDrafts.SingleOrDefaultAsync(
            draft => draft.BranchId == branchId
                     && draft.CustomerId == customerId
                     && draft.TemplateId == templateId
                     && draft.OrganisationId == organisationId
                     && draft.ConsumedAt == null,
            cancellationToken);

    /// <inheritdoc />
    public async Task<MeasurementDraft?> FindDraftAsync(
        Guid draftId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.MeasurementDrafts.SingleOrDefaultAsync(
            draft => draft.Id == draftId && draft.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public async Task<MeasurementVersion?> FindVersionAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.MeasurementVersions.SingleOrDefaultAsync(
            version => version.Id == versionId && version.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public async Task<int> NextVersionNumberAsync(
        Guid customerId,
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var highest = await context.MeasurementVersions
            .Where(version => version.CustomerId == customerId && version.TemplateId == templateId)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(cancellationToken);

        return (highest ?? 0) + 1;
    }

    /// <inheritdoc />
    public void Add(MeasurementDraft draft) => context.MeasurementDrafts.Add(draft);

    /// <inheritdoc />
    public void Add(MeasurementVersion version) => context.MeasurementVersions.Add(version);

    /// <inheritdoc />
    public EntityTag EntityTagOf(MeasurementDraft draft) => context.EntityTagOf(draft);

    /// <inheritdoc />
    public async Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two people on one branch measuring one garment between them. Last-writer-wins here would silently
            // drop half of it, so the loser is told and re-reads.
            return Result.Failure(MeasurementErrors.DraftChanged);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CustomersDbContext.OneOpenDraftIndex,
            })
        {
            // Two counters started measuring the same customer against the same template in the same instant.
            // Nothing was created; the one that lost re-reads and finds the other's draft.
            return Result.Failure(MeasurementErrors.DraftAlreadyOpen);
        }
    }

    /// <inheritdoc />
    public async Task<Result> SaveConfirmationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // The draft moved between the read and the write — including the case that matters most, another
            // confirmation consuming it first. INV-MSR-02: a conflict, never a second version.
            return Result.Failure(MeasurementErrors.DraftAlreadyConfirmed);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CustomersDbContext.MeasurementVersionNumberIndex,
            })
        {
            // Two confirmations for one customer read the same maximum. Nothing was created, so asking again
            // takes the number after — and the retry key means the caller reaches one version either way.
            return Result.Failure(MeasurementErrors.VersionNumberConflict);
        }
    }
}
