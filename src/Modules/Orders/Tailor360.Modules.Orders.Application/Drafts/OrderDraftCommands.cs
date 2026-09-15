using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Domain.Jobs;
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

/// <summary>Adds a garment section.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="CategoryKey">The garment category, as a catalogue key.</param>
/// <param name="ServiceTypeKey">The service type within that category, as a catalogue key.</param>
/// <param name="DesignSelectionDraftId">The Catalog-owned design selection draft, where one exists.</param>
/// <param name="MeasurementIntent">What the section says about its measurements.</param>
/// <param name="MeasurementVersionId">The confirmed version being reused, where one is named.</param>
/// <param name="DueDate">The promised date for this garment, as a branch-local date.</param>
/// <param name="Instructions">Free-text craft instructions, where any were given.</param>
/// <param name="ReferenceMediaIds">Reference and material images, by Media id.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="By">Who added it.</param>
public sealed record AddOrderDraftGarmentCommand(
    Guid DraftId,
    string? CategoryKey,
    string? ServiceTypeKey,
    Guid? DesignSelectionDraftId,
    MeasurementIntent MeasurementIntent,
    Guid? MeasurementVersionId,
    DateOnly? DueDate,
    string? Instructions,
    IReadOnlyCollection<Guid>? ReferenceMediaIds,
    Guid OrganisationId,
    Guid? By);

/// <summary>Replaces the whole content of a garment section, leaving its identity, position and dependencies alone.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="GarmentId">The section being saved.</param>
/// <param name="CategoryKey">The garment category, as a catalogue key.</param>
/// <param name="ServiceTypeKey">The service type within that category, as a catalogue key.</param>
/// <param name="DesignSelectionDraftId">The Catalog-owned design selection draft, where one exists.</param>
/// <param name="MeasurementIntent">What the section says about its measurements.</param>
/// <param name="MeasurementVersionId">The confirmed version being reused, where one is named.</param>
/// <param name="DueDate">The promised date for this garment, as a branch-local date.</param>
/// <param name="Instructions">Free-text craft instructions, where any were given.</param>
/// <param name="ReferenceMediaIds">Reference and material images, by Media id.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the garment section was read against.</param>
/// <param name="By">Who saved it.</param>
public sealed record SaveOrderDraftGarmentCommand(
    Guid DraftId,
    Guid GarmentId,
    string? CategoryKey,
    string? ServiceTypeKey,
    Guid? DesignSelectionDraftId,
    MeasurementIntent MeasurementIntent,
    Guid? MeasurementVersionId,
    DateOnly? DueDate,
    string? Instructions,
    IReadOnlyCollection<Guid>? ReferenceMediaIds,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? By);

/// <summary>Removes a garment section and every dependency naming it, in either direction.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="GarmentId">The section being removed.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the garment section was read against.</param>
/// <param name="By">Who removed it.</param>
public sealed record RemoveOrderDraftGarmentCommand(
    Guid DraftId,
    Guid GarmentId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? By);

/// <summary>Declares that one section waits for, or is delivered with, another section of the same draft.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="GarmentId">The dependent section.</param>
/// <param name="PrerequisiteGarmentId">The section it depends on.</param>
/// <param name="Kind">Which relationship is being declared.</param>
/// <param name="Reason">Why, where a reason was given.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the dependent garment section was read against.</param>
/// <param name="By">Who declared it.</param>
public sealed record DeclareOrderDraftDependencyCommand(
    Guid DraftId,
    Guid GarmentId,
    Guid PrerequisiteGarmentId,
    JobDependencyKind Kind,
    string? Reason,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? By);

/// <summary>Withdraws a dependency one section declared on another.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="GarmentId">The dependent section.</param>
/// <param name="PrerequisiteGarmentId">The section it named.</param>
/// <param name="Kind">The relationship that was declared.</param>
/// <param name="OrganisationId">The organisation.</param>
/// <param name="ExpectedVersion">The tag the dependent garment section was read against.</param>
/// <param name="By">Who withdrew it.</param>
public sealed record WithdrawOrderDraftDependencyCommand(
    Guid DraftId,
    Guid GarmentId,
    Guid PrerequisiteGarmentId,
    JobDependencyKind Kind,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? By);

/// <summary>A draft, one of its garment sections, and the tag an edit to that section must be made against.</summary>
/// <param name="Draft">The draft.</param>
/// <param name="Garment">The section.</param>
/// <param name="Tag">The section's own <c>xmin</c>, as the <c>ETag</c> the client sends back as <c>If-Match</c>.</param>
public sealed record CapturedOrderDraftGarment(OrderDraft Draft, OrderDraftGarment Garment, EntityTag Tag);
