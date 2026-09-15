using Tailor360.Modules.Orders.Domain.Drafts;

namespace Tailor360.Modules.Orders.Api.Payloads;

/// <summary>
/// An order draft as a screen reads it.
/// </summary>
/// <remarks>
/// ARCH-013: <see cref="OrderDraft"/> is never returned directly, and every field here is a public
/// payload type declared in this <c>Api</c> project. <see cref="CustomerId"/> is an identifier only —
/// no name, no telephone number (<c>OrderDraft.CustomerId</c>'s own remarks and CLAUDE.md section 4
/// rule 8) — and <see cref="StartedBy"/> / <see cref="UpdatedBy"/> are likewise identifiers, never a
/// display name. Garment sections are not carried yet: #199's garment-and-dependency half adds them,
/// together with the payload type that describes one.
/// </remarks>
/// <param name="OrderDraftId">Identity of the draft.</param>
/// <param name="CustomerId">The customer it is being built for, as an identifier.</param>
/// <param name="BranchId">The branch taking the order.</param>
/// <param name="DueDate">The promised date for the order as a whole, or null.</param>
/// <param name="Notes">What the counter wrote about the order as a whole, or null.</param>
/// <param name="StartedAt">When the draft was started, in UTC.</param>
/// <param name="StartedBy">Who started it, as an identifier, or null.</param>
/// <param name="UpdatedAt">When it was last written to, in UTC.</param>
/// <param name="UpdatedBy">Who last wrote to it, as an identifier, or null.</param>
/// <param name="ExpiresAt">When it stops being work in progress, in UTC.</param>
/// <param name="ConsumedAt">When it became an order, or null while it is still work in progress.</param>
/// <param name="IsOpen">Whether the draft has yet to become an order.</param>
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
    bool IsOpen)
{
    /// <summary>Projects a draft onto the payload a screen reads.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>The payload.</returns>
    public static OrderDraftPayload From(OrderDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

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
            draft.IsOpen);
    }
}
