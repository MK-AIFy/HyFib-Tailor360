namespace Tailor360.Platform.Security.Background;

/// <summary>
/// Declares that a type is a background job and states, in one place, what it is allowed to do: the
/// permissions it exercises, how far across branches it reaches, and whether it runs as the system or
/// on behalf of the person who queued it.
/// </summary>
/// <remarks>
/// <para>
/// A background job has no request and therefore no ambient user, so nothing about it is implied.
/// Without this declaration a job would either run as nobody — and fail on the first authorisation
/// check for a reason nobody can read — or run as everybody, which is how a scheduled task quietly
/// becomes the widest principal in the system. The declaration is what
/// <see cref="IWorkerScopeFactory"/> builds a principal from, and an architecture test fails a hosted
/// service that carries no declaration at all.
/// </para>
/// <para>
/// The permissions named here are the job's ceiling, not a grant of trust: a job acting for a requester
/// receives them only after the requester has been shown to still hold every one of them.
/// </para>
/// </remarks>
/// <param name="name">
/// The stable job name, in the same <c>module.job</c> shape as a job lease, for example
/// <c>platform.outbox_dispatcher</c>. It appears in the audit trail as the actor for system work.
/// </param>
/// <param name="branchScope">How far across branches the job reaches.</param>
/// <param name="permissions">
/// The permission keys the job exercises. Every one must exist in the catalogue; a job that needs none
/// declares none and receives a principal that holds nothing.
/// </param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class WorkerJobAttribute(
    string name,
    WorkerBranchScope branchScope,
    params string[] permissions) : Attribute
{
    /// <summary>The stable job name.</summary>
    public string Name { get; } = name;

    /// <summary>How far across branches the job reaches.</summary>
    public WorkerBranchScope BranchScope { get; } = branchScope;

    /// <summary>The permission keys the job exercises.</summary>
    public IReadOnlyList<string> Permissions { get; } = permissions ?? [];

    /// <summary>
    /// True when the job runs on behalf of the user who queued it rather than as the system. Such a
    /// job may only be started through <see cref="IWorkerScopeFactory.CreateScopeForAsync"/>, which
    /// rebuilds the requester's authority from the database before the job runs.
    /// </summary>
    public bool ActsForRequester { get; init; }
}

/// <summary>
/// How far across branches a background job reaches. It is deliberately not
/// <see cref="Abstractions.Multitenancy.BranchScope"/>: an endpoint always acts inside the caller's
/// current branch, whereas a job may legitimately touch no branch-owned data at all, and a scope of
/// "none" is the declaration that says so rather than leaving it to be inferred.
/// </summary>
public enum WorkerBranchScope
{
    /// <summary>The job touches no branch-owned data. It may act in no branch.</summary>
    None = 0,

    /// <summary>The job runs for one named branch, supplied when the scope is opened.</summary>
    OneBranch = 1,

    /// <summary>
    /// The job runs across the branches its requester is assigned to. Only meaningful for a job that
    /// acts for a requester, because the system is assigned to no branch.
    /// </summary>
    AssignedBranches = 2,

    /// <summary>
    /// The job reaches every branch in the organisation. The principal carries
    /// <c>admin.organisation.read_all_branches</c> for that reason, and a job acting for a requester
    /// receives it only when the requester still holds it.
    /// </summary>
    Organisation = 3,
}
