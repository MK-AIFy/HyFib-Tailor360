using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Application.Drafts;

/// <summary>Persistence for branch-owned order intake.</summary>
public interface IOrderDraftStore
{
    Task<OrderDraft?> FindAsync(
        Guid draftId, Guid organisationId, Guid branchId, CancellationToken cancellationToken);

    void Add(OrderDraft draft);

    EntityTag EntityTagOf(OrderDraft draft);

    Task<Result> SaveAsync(CancellationToken cancellationToken);
}
