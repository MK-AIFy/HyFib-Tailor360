using Tailor360.Modules.Orders.Application.Drafts;
using Tailor360.Modules.Orders.Domain.Drafts;

namespace Tailor360.Modules.Orders.Api;

/// <summary>Start branch intake for a customer.</summary>
public sealed record StartOrderDraftRequest(Guid CustomerId);

/// <summary>Add an orderable service to an unpriced draft.</summary>
public sealed record AddDraftGarmentRequest(Guid ServiceTypeId, int Quantity, string? Notes);

/// <summary>The most recently edited active drafts in the current branch.</summary>
public sealed record RecentOrderDraftsPayload(IReadOnlyList<RecentOrderDraftPayload> Drafts);

/// <summary>A branch draft that can be resumed at intake.</summary>
public sealed record RecentOrderDraftPayload(
    Guid DraftId,
    string CustomerNumber,
    string CustomerName,
    int GarmentCount,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt)
{
    public static RecentOrderDraftPayload From(RecentOrderDraft draft) => new(
        draft.DraftId,
        draft.CustomerNumber,
        draft.CustomerName,
        draft.GarmentCount,
        draft.UpdatedAt,
        draft.ExpiresAt);
}

/// <summary>The unpriced draft shown to counter staff.</summary>
public sealed record OrderDraftPayload(
    Guid DraftId,
    Guid CustomerId,
    string CustomerNumber,
    string CustomerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<DraftGarmentPayload> Garments)
{
    public static OrderDraftPayload From(OrderDraft draft) => new(
        draft.Id,
        draft.CustomerId,
        draft.CustomerNumber,
        draft.CustomerName,
        draft.CreatedAt,
        draft.UpdatedAt,
        draft.ExpiresAt,
        [.. draft.Garments.Select(DraftGarmentPayload.From)]);
}

/// <summary>One pinned catalogue selection in the draft.</summary>
public sealed record DraftGarmentPayload(
    Guid GarmentId,
    Guid ServiceTypeId,
    Guid CatalogVersionId,
    string CategoryCode,
    string ServiceCode,
    string ServiceName,
    int Quantity,
    string? Notes)
{
    public static DraftGarmentPayload From(DraftGarment garment) => new(
        garment.Id,
        garment.ServiceTypeId,
        garment.CatalogVersionId,
        garment.CategoryCode,
        garment.ServiceCode,
        garment.ServiceName,
        garment.Quantity,
        garment.Notes);
}
