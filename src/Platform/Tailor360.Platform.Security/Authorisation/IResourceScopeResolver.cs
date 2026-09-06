namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Loads the branch and the assignments of one kind of resource, so that authorisation can be decided
/// against the resource rather than against the caller's own claims.
/// </summary>
/// <remarks>
/// <para>
/// A module implements one resolver per resource kind it exposes on a route and registers it in its own
/// service-collection extension. The platform never queries a module's tables; it asks the module, and
/// the module answers with identifiers only.
/// </para>
/// <para>
/// A resolver is called once per request, before the authorisation handlers, and its answer is cached in
/// <see cref="ResourceScopeContext"/> for the rest of that request. It must be cheap — one indexed read
/// of the owning row — and it must not itself apply any branch filter: returning null for a resource
/// that exists in another branch would make "not yours" and "not there" the resolver's decision to
/// confuse rather than the pipeline's, and the pipeline is where the audit trail is written.
/// </para>
/// </remarks>
public interface IResourceScopeResolver
{
    /// <summary>
    /// The resource kind this resolver answers for, matching the kind the endpoint declares. Dotted and
    /// module-prefixed, for example <c>orders.garment_job</c>.
    /// </summary>
    string ResourceKind { get; }

    /// <summary>
    /// Loads the scope of one resource, or null when no such resource exists.
    /// </summary>
    /// <param name="resourceId">The identifier taken from the route.</param>
    /// <param name="cancellationToken">Cancels the read when the request is abandoned.</param>
    ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken);
}
