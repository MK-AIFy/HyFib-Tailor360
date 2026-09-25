using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Drafts;

/// <summary>Branch-owned intake work. A draft has no price, order number or job card.</summary>
public sealed class OrderDraft
{
    private readonly List<DraftGarment> _garments = [];

    private OrderDraft() { }

    private OrderDraft(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        string customerNumber,
        string customerName,
        DateTimeOffset now,
        Guid actorId)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CustomerId = customerId;
        CustomerNumber = customerNumber;
        CustomerName = customerName;
        CreatedAt = now;
        UpdatedAt = now;
        ExpiresAt = now.AddHours(72);
        CreatedBy = actorId;
        UpdatedBy = actorId;
    }

    public Guid Id { get; private set; }
    public Guid OrganisationId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string CustomerNumber { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public IReadOnlyList<DraftGarment> Garments => _garments;

    public static Result<OrderDraft> Start(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        string customerNumber,
        string customerName,
        DateTimeOffset now,
        Guid actorId)
    {
        if (id == Guid.Empty || organisationId == Guid.Empty || branchId == Guid.Empty
            || customerId == Guid.Empty || actorId == Guid.Empty)
        {
            return Result.Failure<OrderDraft>(DraftErrors.InvalidIdentity);
        }

        if (string.IsNullOrWhiteSpace(customerNumber) || string.IsNullOrWhiteSpace(customerName))
        {
            return Result.Failure<OrderDraft>(DraftErrors.CustomerRequired);
        }

        return Result.Success(new OrderDraft(
            id, organisationId, branchId, customerId, customerNumber, customerName, now, actorId));
    }

    public Result AddGarment(
        Guid garmentId,
        Guid serviceTypeId,
        Guid catalogVersionId,
        string categoryCode,
        string serviceCode,
        string serviceName,
        int quantity,
        string? notes,
        DateTimeOffset now,
        Guid actorId)
    {
        if (now >= ExpiresAt)
        {
            return Result.Failure(DraftErrors.Expired);
        }

        if (garmentId == Guid.Empty || serviceTypeId == Guid.Empty || catalogVersionId == Guid.Empty)
        {
            return Result.Failure(DraftErrors.InvalidIdentity);
        }

        if (quantity is < 1 or > 100)
        {
            return Result.Failure(DraftErrors.InvalidQuantity);
        }

        if (notes?.Length > 1000)
        {
            return Result.Failure(DraftErrors.NotesTooLong);
        }

        _garments.Add(new DraftGarment(
            garmentId, Id, serviceTypeId, catalogVersionId, categoryCode,
            serviceCode, serviceName, quantity, notes, now));
        UpdatedAt = now;
        UpdatedBy = actorId;
        return Result.Success();
    }
}

/// <summary>A catalogue selection frozen at the moment it was added to a draft.</summary>
public sealed class DraftGarment
{
    private DraftGarment() { }

    internal DraftGarment(
        Guid id,
        Guid orderDraftId,
        Guid serviceTypeId,
        Guid catalogVersionId,
        string categoryCode,
        string serviceCode,
        string serviceName,
        int quantity,
        string? notes,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrderDraftId = orderDraftId;
        ServiceTypeId = serviceTypeId;
        CatalogVersionId = catalogVersionId;
        CategoryCode = categoryCode;
        ServiceCode = serviceCode;
        ServiceName = serviceName;
        Quantity = quantity;
        Notes = notes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrderDraftId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public Guid CatalogVersionId { get; private set; }
    public string CategoryCode { get; private set; } = string.Empty;
    public string ServiceCode { get; private set; } = string.Empty;
    public string ServiceName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public static class DraftErrors
{
    public static Error InvalidIdentity { get; } = Error.Validation(
        "orders.draft.invalid-identity", "The draft identifiers are invalid.");
    public static Error CustomerRequired { get; } = Error.Validation(
        "orders.draft.customer-required", "Choose a customer before starting the order.", "customerId");
    public static Error CustomerNotFound { get; } = Error.NotFound(
        "orders.draft.customer-not-found", "That customer is not available in this organisation.");
    public static Error CustomerMerged { get; } = Error.Conflict(
        "orders.draft.customer-merged", "This customer record was merged. Find the current record before continuing.");
    public static Error DraftNotFound { get; } = Error.NotFound(
        "orders.draft.not-found", "That order draft could not be found.");
    public static Error Expired { get; } = Error.Conflict(
        "orders.draft.expired", "This draft expired. Start a new intake with current customer and catalogue details.");
    public static Error ServiceUnavailable { get; } = Error.Conflict(
        "orders.draft.service-unavailable", "This service is not in the published catalogue for this branch.");
    public static Error InvalidQuantity { get; } = Error.Validation(
        "orders.draft.invalid-quantity", "Quantity must be between 1 and 100.", "quantity");
    public static Error NotesTooLong { get; } = Error.Validation(
        "orders.draft.notes-too-long", "Notes cannot exceed 1000 characters.", "notes");
    public static Error Changed { get; } = Error.Conflict(
        "orders.draft.changed", "This draft changed. Reload it before saving your garment.");
}
