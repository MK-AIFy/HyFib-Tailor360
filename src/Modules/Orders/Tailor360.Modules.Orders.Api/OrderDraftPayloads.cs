using Tailor360.Modules.Orders.Domain.Drafts;

namespace Tailor360.Modules.Orders.Api;

/// <summary>Start branch intake for a customer.</summary>
public sealed record StartOrderDraftRequest(Guid CustomerId);

/// <summary>Add an orderable service to an unpriced draft.</summary>
public sealed record AddDraftGarmentRequest(Guid ServiceTypeId, int Quantity, string? Notes);

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
