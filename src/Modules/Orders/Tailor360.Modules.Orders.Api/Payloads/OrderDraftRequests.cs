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

/// <summary>Adds a garment section to a draft.</summary>
/// <remarks>
/// Deliberately no <c>catalogVersionId</c> and no <c>measurementTemplateId</c>: both are pinned
/// server-side from what the branch may order today, never trusted from the request
/// (<see cref="DraftRequestParsing"/>'s caller resolves them, not this type).
/// </remarks>
/// <param name="CategoryKey">The garment category, as a catalogue key.</param>
/// <param name="ServiceTypeKey">The service type within that category, as a catalogue key.</param>
/// <param name="DesignSelectionDraftId">The Catalog-owned design selection draft, where one exists.</param>
/// <param name="MeasurementIntent">What the section says about its measurements, by name.</param>
/// <param name="MeasurementVersionId">The confirmed version being reused, where one is named.</param>
/// <param name="DueDate">The promised date for this garment, or null.</param>
/// <param name="Instructions">Free-text craft instructions, where any were given.</param>
/// <param name="ReferenceMediaIds">Reference and material images, by Media id.</param>
public sealed record AddOrderDraftGarmentRequest(
    string? CategoryKey,
    string? ServiceTypeKey,
    Guid? DesignSelectionDraftId,
    string? MeasurementIntent,
    Guid? MeasurementVersionId,
    DateOnly? DueDate,
    string? Instructions,
    IReadOnlyList<Guid>? ReferenceMediaIds);

/// <summary>Replaces the whole content of a garment section.</summary>
/// <param name="CategoryKey">The garment category, as a catalogue key.</param>
/// <param name="ServiceTypeKey">The service type within that category, as a catalogue key.</param>
/// <param name="DesignSelectionDraftId">The Catalog-owned design selection draft, where one exists.</param>
/// <param name="MeasurementIntent">What the section says about its measurements, by name.</param>
/// <param name="MeasurementVersionId">The confirmed version being reused, where one is named.</param>
/// <param name="DueDate">The promised date for this garment, or null.</param>
/// <param name="Instructions">Free-text craft instructions, where any were given.</param>
/// <param name="ReferenceMediaIds">Reference and material images, by Media id.</param>
public sealed record SaveOrderDraftGarmentRequest(
    string? CategoryKey,
    string? ServiceTypeKey,
    Guid? DesignSelectionDraftId,
    string? MeasurementIntent,
    Guid? MeasurementVersionId,
    DateOnly? DueDate,
    string? Instructions,
    IReadOnlyList<Guid>? ReferenceMediaIds);

/// <summary>Declares that one section waits for, or is delivered with, another section of the same draft.</summary>
/// <param name="PrerequisiteGarmentId">The section this one depends on.</param>
/// <param name="Kind">Which relationship is being declared, by name.</param>
/// <param name="Reason">Why, where a reason was given.</param>
public sealed record DeclareOrderDraftDependencyRequest(Guid PrerequisiteGarmentId, string? Kind, string? Reason);

/// <summary>
/// Withdraws a dependency one section declared on another.
/// </summary>
/// <remarks>
/// A body rather than a route segment: the composite primary key
/// <c>(order_draft_garment_id, prerequisite_order_draft_garment_id, kind)</c> makes the <em>triple</em>
/// the row's identity, and a route can carry only the two identifiers.
/// </remarks>
/// <param name="PrerequisiteGarmentId">The section it named.</param>
/// <param name="Kind">The relationship that was declared, by name.</param>
public sealed record WithdrawOrderDraftDependencyRequest(Guid PrerequisiteGarmentId, string? Kind);

/// <summary>
/// Parses a request's wire string against a domain enumeration, called from the endpoint that reads one
/// of the requests above rather than exposed as a member of any of them.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a free function and not a computed property on a request record: ARCH-013's endpoint
/// scan walks a payload type's own member types, and a property returning a <c>Domain</c> enumeration —
/// even a computed one that reads a <c>string</c> field — makes the request type itself "reach" the
/// <c>Domain</c> project, which the rule forbids. Calling this from the endpoint lambda keeps every
/// request record here built from primitives only.
/// </para>
/// <para>
/// Maps anything unrecognised to a value the enumeration does not define, rather than a default member.
/// A cast is exactly what a deserialiser produces from a numeric value it does not recognise, and the
/// domain's own <c>Enum.IsDefined</c> check already exists to answer <c>orders.value-not-understood</c>
/// for that case (<c>OrderDraftGarmentContent.Create</c>, <c>OrderDraftGarment.DeclareDependency</c>) —
/// parsing an unrecognised string the same way reuses that check rather than duplicating it here.
/// </para>
/// </remarks>
internal static class DraftRequestParsing
{
    /// <summary>Parses <paramref name="value"/> against <typeparamref name="TEnum"/>.</summary>
    /// <typeparam name="TEnum">The domain enumeration.</typeparam>
    /// <param name="value">The wire value, or null.</param>
    /// <returns>The parsed member, or a value <typeparamref name="TEnum"/> does not define.</returns>
    public static TEnum ParseOrUndefined<TEnum>(string? value) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : (TEnum)Enum.ToObject(typeof(TEnum), -1);
}
