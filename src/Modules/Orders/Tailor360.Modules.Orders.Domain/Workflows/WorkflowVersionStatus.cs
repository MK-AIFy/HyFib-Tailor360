namespace Tailor360.Modules.Orders.Domain.Workflows;

/// <summary>
/// Where a workflow version stands in its life.
/// </summary>
/// <remarks>
/// <para>
/// The same three-value, one-way lifecycle <see cref="Tailor360.Modules.Catalog.Domain.Catalogue.CatalogStatus"/>
/// carries for a catalogue version, for the reason plan decision D8 gives: a draft becomes published, a published
/// version becomes retired, and nothing ever goes back. Correcting a published version means drafting a new one
/// and publishing that — there is no edge from <see cref="Published"/> back to <see cref="Draft"/>.
/// </para>
/// <para>
/// There is no <c>Deleted</c>. A retired version is still read forever: <c>GarmentJob.WorkflowVersionId</c>
/// (INV-JOB-02) pins a job to the version it was started against, and that pin must render identically no matter
/// how many newer versions the definition has published since.
/// </para>
/// </remarks>
public enum WorkflowVersionStatus
{
    /// <summary>Work in progress. Freely editable, and not what any job may be started against.</summary>
    Draft = 0,

    /// <summary>The version a new job pins. Immutable but for retirement.</summary>
    Published = 1,

    /// <summary>No longer offered to a new job. Still readable, so a job already pinned to it renders unchanged.</summary>
    Retired = 2,
}
