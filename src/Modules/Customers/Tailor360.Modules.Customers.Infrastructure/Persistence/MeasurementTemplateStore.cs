using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// Measurement templates over the <c>customers</c> schema.
/// </summary>
/// <param name="context">The module's context.</param>
public sealed class MeasurementTemplateStore(CustomersDbContext context) : IMeasurementTemplateStore
{
    /// <inheritdoc />
    public async Task<MeasurementTemplate?> FindAsync(
        Guid templateId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.MeasurementTemplates
            .SingleOrDefaultAsync(
                template => template.Id == templateId && template.OrganisationId == organisationId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<MeasurementTemplate?> FindByCodeAsync(
        string code,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.MeasurementTemplates
            .SingleOrDefaultAsync(
                template => template.Code == code && template.OrganisationId == organisationId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<MeasurementTemplate?> FindByVersionAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var templateId = await context.TemplateVersions
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(version => version.Id == versionId && version.OrganisationId == organisationId)
            .Select(version => (Guid?)version.MeasurementTemplateId)
            .SingleOrDefaultAsync(cancellationToken);

        return templateId is { } id ? await FindAsync(id, organisationId, cancellationToken) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MeasurementTemplate>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.MeasurementTemplates
            .Where(template => template.OrganisationId == organisationId)
            .OrderBy(template => template.Code)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> AnyAsync(Guid organisationId, CancellationToken cancellationToken = default)
        => await context.MeasurementTemplates
            .IgnoreAutoIncludes()
            .AnyAsync(template => template.OrganisationId == organisationId, cancellationToken);

    /// <inheritdoc />
    public void Add(MeasurementTemplate template) => context.MeasurementTemplates.Add(template);

    /// <inheritdoc />
    public EntityTag EntityTagOf(MeasurementTemplate template) => context.EntityTagOf(template);

    /// <inheritdoc />
    public async Task<int> SaveAsync(CancellationToken cancellationToken = default)
        => await context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Result> SaveNewAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CustomersDbContext.TemplateCodeIndex,
            })
        {
            return Result.Failure(MeasurementErrors.TemplateCodeTaken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CustomersDbContext.TemplateVersionNumberIndex,
            })
        {
            // Two administrators started a draft of the same template in the same moment and read the same
            // maximum. Nothing was created, so asking again takes the number after.
            return Result.Failure(MeasurementErrors.VersionNumberConflict);
        }
    }

    /// <inheritdoc />
    public async Task<Result> SavePublicationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: CustomersDbContext.OnePublishedTemplateVersionIndex,
            })
        {
            // Two administrators published different versions of one template at the same instant. The database
            // settled it; the loser is told plainly rather than shown a five hundred.
            return Result.Failure(MeasurementErrors.PublishConflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(MeasurementErrors.VersionChanged);
        }
    }
}
