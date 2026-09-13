namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// The resource kinds the design selection draft routes scope their authorisation to (#30, issue #140).
/// </summary>
/// <remarks>
/// In <c>Application</c> rather than beside the resolver, for the reason <c>MeasurementResourceKinds</c>
/// gives: an endpoint in <c>Api</c> declares the kind and a resolver in <c>Infrastructure</c> answers for
/// it, and they are in projects that may not see each other. This is the module's first
/// <c>IResourceScopeResolver</c> — the catalogue itself remains organisation-wide configuration with none
/// — because a draft, unlike a catalogue version, belongs to exactly one branch for its life.
/// </remarks>
public static class DesignSelectionResourceKinds
{
    /// <summary>A garment's design choices in progress, owned by the branch choosing.</summary>
    public const string DesignSelectionDraft = "catalog.design_selection_draft";
}
