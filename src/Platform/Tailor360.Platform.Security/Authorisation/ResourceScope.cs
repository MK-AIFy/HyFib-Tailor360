namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// What the application knows about the resource a request names, loaded from the module that owns it
/// before the authorisation handlers run.
/// </summary>
/// <remarks>
/// <para>
/// Branch scope is <b>evaluated against the resource, never inferred from the caller</b>. A caller's
/// claims say which branches to check against; they never say which branch the thing being asked for
/// belongs to. Without this record the only available check is whether the caller's active branch is
/// one of their own assigned branches — a self-consistency check that every signed-in user passes, and
/// which would let a Coimbatore tailor open a Chennai job by editing the identifier in the address bar.
/// </para>
/// <para>
/// It carries identifiers and nothing else. No customer name, no job number, no phase: this record
/// reaches the authorisation pipeline and, through it, the denial log, and neither is a place for
/// personal or operational data.
/// </para>
/// </remarks>
/// <param name="Kind">The resource kind, matching the resolver that produced it — for example <c>orders.garment_job</c>.</param>
/// <param name="ResourceId">The resource's own identifier.</param>
/// <param name="BranchId">The branch that owns the resource.</param>
/// <param name="AssignedUserIds">
/// The users the resource is assigned to, empty when the resource has no notion of assignment. This is
/// what <see cref="ResourceOwnershipRequirement"/> reads.
/// </param>
public sealed record ResourceScope(
    string Kind,
    Guid ResourceId,
    Guid BranchId,
    IReadOnlySet<Guid> AssignedUserIds)
{
    /// <summary>A resource with no assignments, for kinds where ownership is not a concept.</summary>
    public static ResourceScope Unassigned(string kind, Guid resourceId, Guid branchId)
        => new(kind, resourceId, branchId, new HashSet<Guid>());

    /// <summary>True when <paramref name="userId"/> is one of the resource's assignees.</summary>
    public bool IsAssignedTo(Guid userId) => AssignedUserIds.Contains(userId);
}

/// <summary>Why the pipeline does or does not hold a resource for this request.</summary>
public enum ResourceScopeStatus
{
    /// <summary>
    /// The resolution step has not run. This is the initial value and it is a refusal, not a pass: a
    /// request whose endpoint demands a resource check but whose pipeline never performed one is a
    /// wiring defect, and the safe answer to a wiring defect is no.
    /// </summary>
    Unresolved = 0,

    /// <summary>The resolution step ran and this endpoint names no resource.</summary>
    NotRequired = 1,

    /// <summary>The resource was found and its scope is known.</summary>
    Resolved = 2,

    /// <summary>
    /// The endpoint named a resource that does not exist, or whose identifier was not well formed.
    /// Answered exactly as an out-of-reach resource is, so that neither can be told from the other.
    /// </summary>
    NotFound = 3,
}
