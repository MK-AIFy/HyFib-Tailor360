using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Orders.Application.Drafts;

/// <summary>Starts and edits unpriced order drafts. Confirmation waits for pricing and custody.</summary>
public sealed class OrderDraftHandler(
    IOrderDraftStore store,
    ICustomerSnapshotQuery customers,
    ICatalogAvailabilityQuery catalog,
    IClock clock,
    IIdGenerator ids)
{
    public async Task<Result<OrderDraftState>> StartAsync(
        Guid customerId,
        Guid organisationId,
        Guid branchId,
        Guid actorId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty)
        {
            return Result.Failure<OrderDraftState>(DraftErrors.CustomerRequired);
        }

        var customer = await customers.GetForOrganisationAsync(
            customerId, organisationId, permissions, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<OrderDraftState>(DraftErrors.CustomerNotFound);
        }

        if (customer.MergedIntoCustomerId is not null)
        {
            return Result.Failure<OrderDraftState>(DraftErrors.CustomerMerged);
        }

        var started = OrderDraft.Start(
            ids.NewId(), organisationId, branchId, customerId,
            customer.CustomerNumber, customer.DisplayName, clock.UtcNow, actorId);
        if (started.IsFailure)
        {
            return Result.Failure<OrderDraftState>(started.Error);
        }

        store.Add(started.Value);
        var saved = await store.SaveAsync(cancellationToken);
        return saved.IsFailure
            ? Result.Failure<OrderDraftState>(saved.Error)
            : Result.Success(new OrderDraftState(started.Value, store.EntityTagOf(started.Value)));
    }

    public async Task<Result<OrderDraftState>> ReadAsync(
        Guid draftId,
        Guid organisationId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var draft = await store.FindAsync(draftId, organisationId, branchId, cancellationToken);
        return draft is null
            ? Result.Failure<OrderDraftState>(DraftErrors.DraftNotFound)
            : Result.Success(new OrderDraftState(draft, store.EntityTagOf(draft)));
    }

    public async Task<Result<OrderDraftState>> AddGarmentAsync(
        Guid draftId,
        Guid organisationId,
        Guid branchId,
        Guid actorId,
        Guid serviceTypeId,
        int quantity,
        string? notes,
        EntityTag expected,
        CancellationToken cancellationToken)
    {
        var draft = await store.FindAsync(draftId, organisationId, branchId, cancellationToken);
        if (draft is null)
        {
            return Result.Failure<OrderDraftState>(DraftErrors.DraftNotFound);
        }

        if (!expected.Matches(store.EntityTagOf(draft)))
        {
            return Result.Failure<OrderDraftState>(DraftErrors.Changed);
        }

        var available = await catalog.GetOrderableCatalogAsync(
            organisationId, branchId, clock.UtcNow, cancellationToken);
        var service = available.Services.SingleOrDefault(item => item.ServiceTypeId == serviceTypeId);
        if (service is null || available.VersionId is null)
        {
            return Result.Failure<OrderDraftState>(DraftErrors.ServiceUnavailable);
        }

        var added = draft.AddGarment(
            ids.NewId(), service.ServiceTypeId, available.VersionId.Value,
            service.CategoryCode, service.ServiceCode, service.ServiceName,
            quantity, notes, clock.UtcNow, actorId);
        if (added.IsFailure)
        {
            return Result.Failure<OrderDraftState>(added.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        return saved.IsFailure
            ? Result.Failure<OrderDraftState>(saved.Error)
            : Result.Success(new OrderDraftState(draft, store.EntityTagOf(draft)));
    }
}

/// <summary>The draft with its current strong concurrency token.</summary>
public sealed record OrderDraftState(OrderDraft Draft, EntityTag Tag);
