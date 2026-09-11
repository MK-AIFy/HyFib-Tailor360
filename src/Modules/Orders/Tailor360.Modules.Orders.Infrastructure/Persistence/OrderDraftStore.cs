using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// Order drafts over the Orders context.
/// </summary>
/// <param name="context">The module's context.</param>
public sealed class OrderDraftStore(OrdersDbContext context) : IOrderDraftStore
{
    /// <inheritdoc />
    /// <remarks>
    /// Two collection levels on one aggregate — the garment sections and each section's declared dependencies —
    /// so a single query would return the Cartesian product of them, every section repeated once per dependency
    /// its siblings declared. EF Core warns about exactly this; splitting is safe here because the query names
    /// one row by its key and so cannot see a different set of children between the round trips than a single
    /// query would have. It is the reasoning <c>CustomerStore.FindAsync</c> records for aliases and visibility.
    /// </remarks>
    public Task<OrderDraft?> FindAsync(
        Guid orderDraftId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => context.OrderDrafts
            .Include(draft => draft.Garments)
            .ThenInclude(garment => garment.Dependencies)
            .AsSplitQuery()
            .FirstOrDefaultAsync(
                draft => draft.Id == orderDraftId && draft.OrganisationId == organisationId,
                cancellationToken);

    /// <inheritdoc />
    public void Add(OrderDraft draft) => context.OrderDrafts.Add(draft);

    /// <inheritdoc />
    public EntityTag EntityTagOf(OrderDraft draft) => context.EntityTagOf(draft);

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
            // Two counters building one order between them, each with a garment section open. The draft is
            // locked per section — module-ownership.md section 5.5 — so the row that moved is a section rather
            // than the whole draft, and last-writer-wins here would silently drop one of them.
            return Result.Failure(OrdersErrors.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (OrdersWriteFailures.TryMap(exception, out var error))
        {
            // Every named constraint in the schema and not only this store's, because the three stores share one
            // context and therefore one flush. It carries both of the readings that used to be written out here
            // — a garment identity added to one draft twice is GarmentAlreadyOnDraft, because OrderDraft.AddGarment
            // refuses a reused identifier rather than treating it as a save; two sections taking the same position
            // is a concurrency conflict, because NextPosition() is a max-plus-one read followed by a write — and
            // it answers the rest of the schema's constraints the same way whichever store's save happened to
            // flush them.
            return Result.Failure(error);
        }
    }
}
