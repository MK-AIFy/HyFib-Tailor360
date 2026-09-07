namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// The resource resolved for the current request, filled once by the resolution step that runs between
/// routing and authorisation, and read by the requirement handlers.
/// </summary>
/// <remarks>
/// It mirrors <see cref="Authentication.SessionContext"/> deliberately: one scoped holder, written once
/// early in the pipeline, read by everything downstream, and starting in the state that refuses. Code
/// that runs where the resolution step never ran sees <see cref="ResourceScopeStatus.Unresolved"/>,
/// which every handler treats as a denial rather than as "no resource to check".
/// </remarks>
public sealed class ResourceScopeContext
{
    /// <summary>Why the request does or does not carry a resolved resource.</summary>
    public ResourceScopeStatus Status { get; private set; } = ResourceScopeStatus.Unresolved;

    /// <summary>The resolved resource, or null when there is none.</summary>
    public ResourceScope? Scope { get; private set; }

    /// <summary>Records that this endpoint names no resource to check.</summary>
    public void SetNotRequired()
    {
        Status = ResourceScopeStatus.NotRequired;
        Scope = null;
    }

    /// <summary>Records the resource the request named.</summary>
    public void SetResolved(ResourceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        Status = ResourceScopeStatus.Resolved;
        Scope = scope;
    }

    /// <summary>Records that the identifier the request named matched no resource.</summary>
    public void SetNotFound()
    {
        Status = ResourceScopeStatus.NotFound;
        Scope = null;
    }
}
