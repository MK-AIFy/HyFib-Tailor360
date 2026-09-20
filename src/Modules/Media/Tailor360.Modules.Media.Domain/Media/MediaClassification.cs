namespace Tailor360.Modules.Media.Domain.Media;

/// <summary>
/// The strictest data class an object's bytes carry (<c>docs/nfr/data-classification.md</c> §2, §5.5,
/// §5.6). Derived from <see cref="MediaPurpose"/> by <see cref="MediaObject"/>, never supplied by a
/// caller: whether a photograph shows a person is not something this system can tell from the bytes,
/// so <see cref="Material"/>, <see cref="Reference"/>, <see cref="QcEvidence"/> and
/// <see cref="DeliveryEvidence"/> are classified at the strictest value the class can take — Sensitive
/// Personal — rather than guessed down to Personal. A bundled diagram or illustration shows nobody and
/// is classified Public.
/// </summary>
public enum MediaClassification
{
    /// <summary>Deliberately customer-facing, no personal content — the bundled diagrams and illustrations.</summary>
    Public = 0,

    /// <summary>Personal data about an identified customer, with no image of a person (data-classification.md §2 does not apply this class to any Media purpose today; kept for completeness and a future purpose that needs it).</summary>
    Personal = 1,

    /// <summary>Personal data that may show a person, a child, or a garment worn by one — material, reference, QC and delivery evidence images.</summary>
    SensitivePersonal = 2,
}
