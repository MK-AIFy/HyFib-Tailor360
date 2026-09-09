namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// Where a template version sits in its life.
/// </summary>
/// <remarks>
/// <para>
/// One direction only: <see cref="Draft"/> → <see cref="InReview"/> → <see cref="Published"/> →
/// <see cref="Retired"/>. A version never goes back, because a measurement captured against a published version
/// renders through it forever; letting it return to draft would let somebody change the labels, the units or the
/// rules that a customer's stored measurements are read under.
/// </para>
/// <para>
/// The numbers are stored, so they are fixed. <see cref="InReview"/> sits between drafting and publication because
/// the template is reviewed by a second person — the submitter does not also approve.
/// </para>
/// </remarks>
public enum TemplateStatus
{
    /// <summary>Being written. Freely editable, captures nothing.</summary>
    Draft = 0,

    /// <summary>Submitted and awaiting a second administrator. No longer editable.</summary>
    InReview = 1,

    /// <summary>The version measurements are captured against. Immutable.</summary>
    Published = 2,

    /// <summary>Superseded or withdrawn. Immutable, and still rendered for everything captured under it.</summary>
    Retired = 3,
}
