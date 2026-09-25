using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Persistence.Concurrency;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>Persists order drafts and catches concurrent edits.</summary>
public sealed class OrderDraftStore(OrdersDbContext context) : IOrderDraftStore
{
    public async Task<OrderDraft?> FindAsync(
        Guid draftId, Guid organisationId, Guid branchId, CancellationToken cancellationToken)
        => await context.OrderDrafts
            .Include(draft => draft.Garments)
            .SingleOrDefaultAsync(draft => draft.Id == draftId
                && draft.OrganisationId == organisationId
                && draft.BranchId == branchId, cancellationToken);

    public void Add(OrderDraft draft) => context.OrderDrafts.Add(draft);

    public EntityTag EntityTagOf(OrderDraft draft) => context.EntityTagOf(draft);

    public async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(DraftErrors.Changed);
        }
    }
}
