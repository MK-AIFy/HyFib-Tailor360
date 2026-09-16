using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Modules.Orders.Api.Payloads;

/// <summary>
/// An order draft as a screen reads it.
/// </summary>
/// <remarks>
/// ARCH-013: <see cref="OrderDraft"/> is never returned directly, and every field here is a public
/// payload type declared in this <c>Api</c> project. <see cref="CustomerId"/> is an identifier only —
/// no name, no telephone number (<c>OrderDraft.CustomerId</c>'s own remarks and CLAUDE.md section 4
/// rule 8) — and <see cref="StartedBy"/> / <see cref="UpdatedBy"/> are likewise identifiers, never a
/// display name.
/// </remarks>
/// <param name="OrderDraftId">Identity of the draft.</param>
/// <param name="CustomerId">The customer it is being built for, as an identifier.</param>
/// <param name="BranchId">The branch taking the order.</param>
/// <param name="DueDate">The promised date for the order as a whole, or null.</param>
/// <param name="Notes">What the counter wrote about the order as a whole, or null.</param>
/// <param name="StartedAt">When the draft was started, in UTC.</param>
/// <param name="StartedBy">Who started it, as an identifier, or null.</param>
/// <param name="UpdatedAt">
/// When the draft's own fields — its customer, its schedule, which sections it has — were last written to,
/// in UTC. A section's own edits move that section's <see cref="OrderDraftGarmentPayload.UpdatedAt"/> and
/// <see cref="OrderDraftGarmentPayload.Version"/>, not this.
/// </param>
/// <param name="UpdatedBy">Who last wrote to those fields, as an identifier, or null.</param>
/// <param name="ExpiresAt">When it stops being work in progress, in UTC.</param>
/// <param name="ConsumedAt">When it became an order, or null while it is still work in progress.</param>
/// <param name="IsOpen">Whether the draft has yet to become an order.</param>
/// <param name="Garments">The garment sections, in display order, each with its own version.</param>
public sealed record OrderDraftPayload(
    Guid OrderDraftId,
    Guid CustomerId,
    Guid BranchId,
    DateOnly? DueDate,
    string? Notes,
    DateTimeOffset StartedAt,
    Guid? StartedBy,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    bool IsOpen,
    IReadOnlyList<OrderDraftGarmentPayload> Garments)
{
    /// <summary>Projects a draft onto the payload a screen reads.</summary>
    /// <param name="draft">The draft.</param>
    /// <param name="garmentTags">Each section's own tag, by section identity.</param>
    /// <returns>The payload.</returns>
    public static OrderDraftPayload From(OrderDraft draft, IReadOnlyDictionary<Guid, EntityTag> garmentTags)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(garmentTags);

        return new OrderDraftPayload(
            draft.Id,
            draft.CustomerId,
            draft.BranchId,
            draft.DueDate,
            draft.Notes,
            draft.StartedAt,
            draft.StartedBy,
            draft.UpdatedAt,
            draft.UpdatedBy,
            draft.ExpiresAt,
            draft.ConsumedAt,
            draft.IsOpen,
            // Garments is already position-ordered on the aggregate (OrderDraft.Garments' own remarks).
            [.. draft.Garments.Select(garment => OrderDraftGarmentPayload.From(garment, garmentTags[garment.Id]))]);
    }
}

/// <summary>One garment section of a draft, as a screen reads it.</summary>
/// <remarks>
/// <para>
/// <see cref="MeasurementIntent"/> and a dependency's <see cref="OrderDraftGarmentDependencyPayload.Kind"/>
/// are serialised as names, matching the <c>varchar(20)</c> <c>HasConversion&lt;string&gt;()</c>
/// persistence — never as a number, which the domain would refuse to round-trip
/// (<c>orders.value-not-understood</c> on a cast a deserialiser makes from an unrecognised value).
/// </para>
/// <para>
/// <see cref="Version"/> is the section's own concurrency token, carried in the body because a draft is
/// read whole and edited by the section: a screen that reopened a draft has every section's content from
/// one read and needs every section's token from the same read, or its first save has nothing to send as
/// <c>If-Match</c>. It is the same value the section's own responses carry as <c>ETag</c> and a
/// <c>409</c> reports as <c>currentVersion</c> — unquoted here, quoted on the wire as a header.
/// </para>
/// </remarks>
/// <param name="OrderDraftGarmentId">Identity of the section.</param>
/// <param name="Position">Display order within the draft, one-based.</param>
/// <param name="CategoryKey">The garment category, as a catalogue key.</param>
/// <param name="ServiceTypeKey">The service type within that category, as a catalogue key.</param>
/// <param name="CatalogVersionId">The catalogue version the section is pinned to.</param>
/// <param name="DesignSelectionDraftId">The Catalog-owned design selection draft, or null.</param>
/// <param name="MeasurementIntent">What the section says about its measurements, by name.</param>
/// <param name="MeasurementVersionId">The confirmed version being reused, or null.</param>
/// <param name="MeasurementTemplateId">The template the garment will be measured against, or null.</param>
/// <param name="DueDate">The promised date for this garment, or null.</param>
/// <param name="Instructions">Free-text craft instructions, or null.</param>
/// <param name="ReferenceMediaIds">Reference and material images, by Media id.</param>
/// <param name="UpdatedAt">When the section was last written to, in UTC.</param>
/// <param name="Version">
/// The section's own version, sent back in double quotes as <c>If-Match</c> on every edit to it.
/// </param>
/// <param name="Dependencies">What this section must wait for, or be delivered with.</param>
public sealed record OrderDraftGarmentPayload(
    Guid OrderDraftGarmentId,
    int Position,
    string CategoryKey,
    string ServiceTypeKey,
    Guid CatalogVersionId,
    Guid? DesignSelectionDraftId,
    string MeasurementIntent,
    Guid? MeasurementVersionId,
    Guid? MeasurementTemplateId,
    DateOnly? DueDate,
    string? Instructions,
    IReadOnlyList<Guid> ReferenceMediaIds,
    DateTimeOffset UpdatedAt,
    string Version,
    IReadOnlyList<OrderDraftGarmentDependencyPayload> Dependencies)
{
    /// <summary>Projects a garment section onto the payload a screen reads.</summary>
    /// <param name="garment">The section.</param>
    /// <param name="tag">The section's own tag.</param>
    /// <returns>The payload.</returns>
    public static OrderDraftGarmentPayload From(OrderDraftGarment garment, EntityTag tag)
    {
        ArgumentNullException.ThrowIfNull(garment);

        return new OrderDraftGarmentPayload(
            garment.Id,
            garment.Position,
            garment.CategoryKey,
            garment.ServiceTypeKey,
            garment.CatalogVersionId,
            garment.DesignSelectionDraftId,
            garment.MeasurementIntent.ToString(),
            garment.MeasurementVersionId,
            garment.MeasurementTemplateId,
            garment.DueDate,
            garment.Instructions,
            garment.ReferenceMediaIds,
            garment.UpdatedAt,
            tag.Version,
            // Dependencies is an IReadOnlyCollection with no ordering guarantee — sorted here so the
            // response is deterministic across reads, ordered on the pair that is the row's identity.
            [.. garment.Dependencies
                .OrderBy(dependency => dependency.PrerequisiteOrderDraftGarmentId)
                .ThenBy(dependency => dependency.Kind)
                .Select(OrderDraftGarmentDependencyPayload.From)]);
    }
}

/// <summary>One dependency a garment section declared, as a screen reads it.</summary>
/// <param name="PrerequisiteOrderDraftGarmentId">The section this one depends on.</param>
/// <param name="Kind">Which relationship this is, by name.</param>
/// <param name="Reason">Why it was declared, or null.</param>
/// <param name="DeclaredAt">When it was declared, in UTC.</param>
public sealed record OrderDraftGarmentDependencyPayload(
    Guid PrerequisiteOrderDraftGarmentId,
    string Kind,
    string? Reason,
    DateTimeOffset DeclaredAt)
{
    /// <summary>Projects a dependency onto the payload a screen reads.</summary>
    /// <param name="dependency">The dependency.</param>
    /// <returns>The payload.</returns>
    public static OrderDraftGarmentDependencyPayload From(OrderDraftGarmentDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        return new OrderDraftGarmentDependencyPayload(
            dependency.PrerequisiteOrderDraftGarmentId,
            dependency.Kind.ToString(),
            dependency.Reason,
            dependency.DeclaredAt);
    }
}
