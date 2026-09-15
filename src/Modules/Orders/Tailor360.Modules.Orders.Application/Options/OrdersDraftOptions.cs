using System.ComponentModel.DataAnnotations;
using Tailor360.Modules.Orders.Domain.Drafts;

namespace Tailor360.Modules.Orders.Application.Options;

/// <summary>
/// How long an order draft stays work in progress before it is too old to confirm.
/// </summary>
/// <remarks>
/// <para>
/// The window is documented as branch configuration — <c>docs/prd/glossary.md</c> section 4 and
/// <c>docs/prd/state-transitions.md</c> section 2.1 both fix the default at 72 hours, and
/// <see cref="OrderDraft.DefaultLifetime"/> carries the same figure with the same citation, "so that
/// the number has exactly one home". No branch-configuration surface exists in <c>src/</c> yet, so this
/// class is the module-level binding the application uses until one does: the default below is bound
/// from that constant rather than restated as a second literal, which is the one deliberate deviation
/// from <c>DesignSelectionDraftOptions</c> and <c>MeasurementCaptureOptions</c>, both of which hard-code
/// their own figure because neither has a domain constant to point at.
/// </para>
/// </remarks>
public sealed class OrdersDraftOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Orders:Draft";

    /// <summary>How long a draft stays work in progress.</summary>
    [Required]
    public TimeSpan DraftLifetime { get; set; } = OrderDraft.DefaultLifetime;

    /// <summary>Whether the lifetime is one a shop could work to.</summary>
    /// <remarks>
    /// The same bounds <c>DesignSelectionDraftOptions.IsLifetimeUsable</c> and
    /// <c>MeasurementCaptureOptions.IsLifetimeUsable</c> apply, for the same reason: a lifetime under an
    /// hour would expire a draft while it is still being built at the counter, and one over thirty days
    /// would let a month-old draft be confirmed as today's.
    /// </remarks>
    public bool IsLifetimeUsable
        => DraftLifetime >= TimeSpan.FromHours(1) && DraftLifetime <= TimeSpan.FromDays(30);
}
