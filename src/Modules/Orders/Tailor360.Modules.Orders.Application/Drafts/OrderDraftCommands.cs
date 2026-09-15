using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Modules.Orders.Application.Drafts;

/// <summary>Starts an order draft.</summary>
/// <param name="CustomerId">The customer it is being built for.</param>
/// <param name="DueDate">The promised date for the order as a whole, or null.</param>
/// <param name="Notes">What the counter wrote about the order as a whole, or null.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="BranchId">The branch taking the order.</param>
/// <param name="By">Who started it.</param>
public sealed record StartOrderDraftCommand(
    Guid CustomerId,
    DateOnly? DueDate,
    string? Notes,
    Guid OrganisationId,
    Guid BranchId,
    Guid? By);

/// <summary>Points a draft at a different customer.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="CustomerId">The customer the draft is really for.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the draft was read against.</param>
/// <param name="By">Who made the correction.</param>
public sealed record SetOrderDraftCustomerCommand(
    Guid DraftId,
    Guid CustomerId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? By);

/// <summary>Sets the order-level promised date and notes, replacing both.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="DueDate">The promised date, or null to clear it.</param>
/// <param name="Notes">What the counter wrote, or null to clear it.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the draft was read against.</param>
/// <param name="By">Who saved it.</param>
public sealed record SetOrderDraftScheduleCommand(
    Guid DraftId,
    DateOnly? DueDate,
    string? Notes,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? By);

/// <summary>A draft and the tag an edit to it must be made against.</summary>
/// <param name="Draft">The draft.</param>
/// <param name="Tag">Its <c>xmin</c>, as the <c>ETag</c> the client sends back as <c>If-Match</c>.</param>
public sealed record CapturedOrderDraft(OrderDraft Draft, EntityTag Tag);
