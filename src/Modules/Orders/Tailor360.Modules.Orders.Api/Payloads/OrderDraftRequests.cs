namespace Tailor360.Modules.Orders.Api.Payloads;

/// <summary>Starts an order draft.</summary>
/// <param name="CustomerId">The customer it is being built for.</param>
/// <param name="DueDate">The promised date for the order as a whole, or null.</param>
/// <param name="Notes">What the counter wrote about the order as a whole, or null.</param>
public sealed record StartOrderDraftRequest(Guid CustomerId, DateOnly? DueDate, string? Notes);

/// <summary>Points a draft at a different customer.</summary>
/// <param name="CustomerId">The customer the draft is really for.</param>
public sealed record SetOrderDraftCustomerRequest(Guid CustomerId);

/// <summary>
/// Sets the order-level promised date and notes, replacing both.
/// </summary>
/// <remarks>
/// A whole-value replace, never a partial update: <c>OrderDraft.SetSchedule</c> writes both fields on
/// every call, including clearing one by sending null, so a request that named only one field would
/// silently clear the other.
/// </remarks>
/// <param name="DueDate">The promised date, or null to clear it.</param>
/// <param name="Notes">What the counter wrote, or null to clear it.</param>
public sealed record SetOrderDraftScheduleRequest(DateOnly? DueDate, string? Notes);
