using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Catalog.Application.Options;

/// <summary>
/// How long a design selection draft stays work in progress before it is too old to build a garment on.
/// </summary>
/// <remarks>
/// Mirrors <c>MeasurementCaptureOptions</c> exactly, including the default: <c>docs/prd/design-options.md</c>
/// section 7 fixes it at "expiring after 24 hours by default, matching the measurement draft in
/// [measurement-templates.md] Section 11", and <c>docs/prd/measurement-templates.md</c> line 524 gives that
/// draft's own figure as 24 hours. The number is a default, not a decision — configurable so answering it
/// differently later is a setting rather than a release.
/// </remarks>
public sealed class DesignSelectionDraftOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Catalog:DesignSelectionDraft";

    /// <summary>How long a draft stays work in progress.</summary>
    [Required]
    public TimeSpan DraftLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Whether the lifetime is one a shop could work to.</summary>
    /// <remarks>
    /// The same bounds <c>MeasurementCaptureOptions.IsLifetimeUsable</c> applies, for the same reason: a
    /// lifetime under an hour would expire a draft while a garment is still being chosen, and one over
    /// thirty days would let a month-old choice be confirmed as today's.
    /// </remarks>
    public bool IsLifetimeUsable
        => DraftLifetime >= TimeSpan.FromHours(1) && DraftLifetime <= TimeSpan.FromDays(30);
}
