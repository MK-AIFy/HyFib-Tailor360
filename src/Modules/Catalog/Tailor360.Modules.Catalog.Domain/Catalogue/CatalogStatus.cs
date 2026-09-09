namespace Tailor360.Modules.Catalog.Domain.Catalogue;

/// <summary>
/// Where a catalogue version stands in its life.
/// </summary>
/// <remarks>
/// <para>
/// The three values are the whole lifecycle from <c>docs/prd/category-hierarchy.md</c> section 7, and
/// the transitions between them are one-way: a draft becomes published, a published version becomes
/// retired, and nothing ever goes back. Correcting a published version means cloning it to a new draft
/// and publishing that, which is why <see cref="Published"/> has no edge back to <see cref="Draft"/>.
/// </para>
/// <para>
/// There is no <c>Deleted</c>. A retired version is still read by every order that was placed against
/// it — a job card, an invoice and a report all render from the version they were pinned to — so
/// removing one would not tidy the catalogue, it would make history unreadable.
/// </para>
/// </remarks>
public enum CatalogStatus
{
    /// <summary>Work in progress. Freely editable, invisible to intake, previewable by administrators.</summary>
    Draft = 0,

    /// <summary>The active configuration. Immutable but for its presentation fields.</summary>
    Published = 1,

    /// <summary>No longer offered. Still readable, so historic orders render exactly as they did.</summary>
    Retired = 2,
}
