using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.ContractTests;

/// <summary>
/// The rules an endpoint's authorisation declaration has to satisfy, applied to any set of endpoints.
/// </summary>
/// <remarks>
/// <para>
/// It is a function over a collection rather than a test body so that the same rules can be applied
/// twice: to the endpoints the application actually publishes, and to deliberately wrong ones. Today the
/// application publishes no endpoint that declares a permission at all, so every rule below would pass
/// over an empty set and prove nothing. The negative controls are what make these tests mean something
/// before the first module ships a route — and what will keep meaning something afterwards, because a
/// detector nobody has seen detect anything is a detector nobody should trust.
/// </para>
/// </remarks>
public static class EndpointAuthorisationInspector
{
    /// <summary>Every way the endpoints given break the declaration rules, as sentences.</summary>
    public static IReadOnlyList<string> Inspect(
        IEnumerable<Endpoint> endpoints,
        PermissionCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(catalogue);

        var complaints = new List<string>();

        foreach (var endpoint in endpoints)
        {
            var permission = endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>();
            var resource = endpoint.Metadata.GetMetadata<ResourceScopeMetadata>();
            var ownership = endpoint.Metadata.GetMetadata<ResourceOwnershipMetadata>();
            var anonymous = endpoint.Metadata.GetMetadata<AnonymousJustificationMetadata>();
            var name = Describe(endpoint);

            if (permission is not null && !catalogue.Contains(permission.PermissionKey))
            {
                // A typo closes the endpoint to everybody rather than opening it, so this never becomes
                // a security incident — it becomes a route nobody can use and nobody can explain.
                complaints.Add(
                    $"{name} demands permission '{permission.PermissionKey}', which no module declares.");
            }

            if (resource is not null && permission is null)
            {
                complaints.Add(
                    $"{name} declares a resource scope and no permission. Deciding which row a caller may "
                    + "reach is not a substitute for deciding whether they may be here at all.");
            }

            if (resource is not null && permission is not null && resource.Scope != permission.Scope)
            {
                complaints.Add(
                    $"{name} declares branch scope {permission.Scope} with its permission and "
                    + $"{resource.Scope} with its resource. The two are one decision.");
            }

            if (resource is not null && anonymous is not null)
            {
                complaints.Add($"{name} is anonymous and declares a resource scope.");
            }

            if (permission is not null && anonymous is not null)
            {
                // AllowAnonymous wins at run time: the authorisation middleware short-circuits on the
                // metadata and never evaluates the policy, so the permission, the branch scope and any
                // resource requirement are all skipped. The endpoint is world-readable while its source
                // — and its matrix row — say it is permission-gated. ARCH-022.
                complaints.Add(
                    $"{name} declares permission '{permission.PermissionKey}' and a justified anonymous "
                    + "exposure. AllowAnonymous wins at run time, so the permission is never evaluated "
                    + "and the route is open; delete whichever of the two is not meant.");
            }

            complaints.AddRange(UnscopedIdentifierComplaints(endpoint, permission, resource, name));

            if (ownership is null)
            {
                continue;
            }

            if (resource is null)
            {
                complaints.Add(
                    $"{name} requires an assignment and declares no resource to read it from.");
            }

            if (permission is null)
            {
                // Both of these verbs call RequireAuthorization, so an endpoint carrying only one of
                // them satisfies ARCH-007's "declares a policy" check while naming no permission at
                // all. That is the one way the deny-by-default rule could be passed without meaning it,
                // and it is closed here.
                complaints.Add(
                    $"{name} requires an assignment and no permission. Narrowing an endpoint to the "
                    + "assignee does not decide who may reach the endpoint.");
            }

            complaints.AddRange(
                from key in ownership.SupervisorPermissions
                where !catalogue.Contains(key)
                select $"{name} names supervising permission '{key}', which no module declares.");
        }

        return complaints;
    }

    /// <summary>
    /// ARCH-023: a permissioned route that names a resource in its path also declares which resource,
    /// or records why the thing it names belongs to no branch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Omitting <c>ScopedToResource</c> does not fail; it silently removes the branch check. The
    /// resolution middleware treats "no declaration" as "nothing to load", and the caller-side branch
    /// requirement then asks only whether the caller's own active branch is one of the caller's own
    /// assignments — which every signed-in person passes. The row's branch is never read, so a tailor in
    /// one branch reaches another branch's record by editing the identifier, and every other gate is
    /// satisfied: the policy is present, the audit filter is present, and the matrix row reconciles
    /// because a blank resource cell agrees with an endpoint that declares none.
    /// </para>
    /// <para>
    /// Organisation-scoped routes are outside the rule: their reach is decided by a permission rather
    /// than by the row's branch, and a route about the organisation may legitimately name something that
    /// belongs to no branch at all.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> UnscopedIdentifierComplaints(
        Endpoint endpoint,
        RequiredPermissionMetadata? permission,
        ResourceScopeMetadata? resource,
        string name)
    {
        if (permission is null
            || resource is not null
            || permission.Scope == BranchScope.Organisation
            || endpoint is not RouteEndpoint route
            || route.RoutePattern.Parameters.Count == 0)
        {
            yield break;
        }

        if (endpoint.Metadata.GetMetadata<UnscopedRouteMetadata>() is not null)
        {
            yield break;
        }

        var parameters = string.Join(", ", route.RoutePattern.Parameters.Select(parameter => parameter.Name));

        yield return
            $"{name} demands '{permission.PermissionKey}' with branch scope {permission.Scope} and names "
            + $"{parameters} in its route, but declares no resource scope. Nothing then loads the row's "
            + "branch, so the branch check passes for every signed-in caller. Add ScopedToResource, or "
            + "TouchesNoBranchOwnedResource with the reason.";
    }

    private static string Describe(Endpoint endpoint)
        => endpoint is RouteEndpoint route
            ? $"{string.Join('|', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} "
              + route.RoutePattern.RawText
            : endpoint.DisplayName ?? "(unnamed endpoint)";
}
