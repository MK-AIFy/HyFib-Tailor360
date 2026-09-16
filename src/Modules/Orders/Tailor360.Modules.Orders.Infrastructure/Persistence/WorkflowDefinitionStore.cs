using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.Workflows;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// Workflow definitions over the Orders context.
/// </summary>
/// <param name="context">The module's context.</param>
public sealed class WorkflowDefinitionStore(OrdersDbContext context) : IWorkflowDefinitionStore
{
    /// <inheritdoc />
    /// <remarks>
    /// One collection deep — <c>Versions</c> — and a version carries no collection of its own that lives in a
    /// second table (its phases are the only one, and are loaded with it below), so this needs none of
    /// <c>OrderDraftStore.FindAsync</c>'s split-query reasoning: there is only one many-to-one shape here, not
    /// two crossed ones.
    /// </remarks>
    public Task<WorkflowDefinition?> FindAsync(
        Guid workflowDefinitionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => context.WorkflowDefinitions
            .Include(definition => definition.Versions)
            .ThenInclude(version => version.Phases)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                definition => definition.Id == workflowDefinitionId && definition.OrganisationId == organisationId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await context.WorkflowDefinitions
            .Include(definition => definition.Versions)
            .ThenInclude(version => version.Phases)
            .AsSplitQuery()
            .Where(definition => definition.OrganisationId == organisationId)
            .OrderByDescending(definition => definition.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<WorkflowVersion?> FindPublishedVersionAsync(
        Guid workflowDefinitionId,
        Guid organisationId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        // The partial unique index guarantees at most one row can ever match "published_at <= at AND (retired_at
        // IS NULL OR retired_at > at)" for a given definition, so this reads without loading every version or
        // its phases — the shape ICatalogAvailabilityQuery.GetServiceAsync reads a single pinned service by.
        var version = await context.Set<WorkflowVersion>()
            .Include(candidate => candidate.Phases)
            .AsSplitQuery()
            .Where(candidate =>
                candidate.WorkflowDefinitionId == workflowDefinitionId
                && candidate.OrganisationId == organisationId
                && candidate.PublishedAt != null
                && candidate.PublishedAt <= at
                && (candidate.RetiredAt == null || candidate.RetiredAt > at))
            .FirstOrDefaultAsync(cancellationToken);

        return version;
    }

    /// <inheritdoc />
    public void Add(WorkflowDefinition definition) => context.WorkflowDefinitions.Add(definition);

    /// <inheritdoc />
    public EntityTag EntityTagOf(WorkflowDefinition definition) => context.EntityTagOf(definition);

    /// <inheritdoc />
    public EntityTag EntityTagOf(WorkflowVersion version) => context.EntityTagOf(version);

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
            // A definition's own row, or one of its versions' — either way, somebody else's write reached the
            // row between this store's read and this save.
            return Result.Failure(OrdersErrors.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (OrdersWriteFailures.TryMap(exception, out var error))
        {
            // Every named constraint in the schema and not only this store's, for the reason
            // OrdersWriteFailures's own remarks give.
            return Result.Failure(error);
        }
    }
}
