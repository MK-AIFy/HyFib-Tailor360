using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.Background;

/// <summary>
/// The caller a background job runs as. Either the system, acting on its own declared authority, or a
/// person the job was queued by, acting on the authority they hold at the moment the job runs.
/// </summary>
/// <remarks>
/// <para>
/// It cannot be constructed from outside this library, and inside it only
/// <see cref="WorkerScopeFactory"/> constructs it. That is the point: a principal that any code could
/// build would be a way to obtain authority without asking for it, and a job's authority has to come
/// from its declaration and — when it acts for someone — from that person's current access.
/// </para>
/// <para>
/// Two properties are deliberately fixed. <see cref="LastReauthenticatedAt"/> is always null, because
/// no job can re-authenticate; anything demanding a fresh factor is therefore refused rather than
/// waved through. <see cref="Permissions"/> is the job's declaration, never the requester's whole
/// permission set, so a job queued by an owner does not run as an owner.
/// </para>
/// </remarks>
public sealed class WorkerPrincipal : ICurrentUser
{
    private static readonly IReadOnlySet<Guid> NoBranches = new HashSet<Guid>();

    private readonly bool _reachesEveryBranch;

    private WorkerPrincipal(
        WorkerJobDescriptor job,
        Guid userId,
        string displayName,
        OrganisationContext context,
        IReadOnlySet<Guid> assignedBranches,
        IReadOnlySet<string> permissions,
        bool reachesEveryBranch,
        bool mfaSatisfied)
    {
        Job = job;
        UserId = userId;
        DisplayName = displayName;
        Context = context;
        AssignedBranches = assignedBranches;
        Permissions = permissions;
        _reachesEveryBranch = reachesEveryBranch;
        MfaSatisfied = mfaSatisfied;
    }

    /// <summary>The declaration this principal was built from.</summary>
    public WorkerJobDescriptor Job { get; }

    /// <summary>True when the job runs as the system rather than for a person.</summary>
    public bool IsSystem => UserId == Guid.Empty;

    /// <inheritdoc />
    /// <remarks>
    /// A job is authenticated in the sense that matters to an authorisation handler: its identity was
    /// established before it started, by the declaration it carries and, when it acts for someone, by
    /// a fresh read of that person's access.
    /// </remarks>
    public bool IsAuthenticated => true;

    /// <inheritdoc />
    public Guid UserId { get; }

    /// <summary>
    /// The identifier used for audit entries and idempotency keys. A job acting for a person uses that
    /// person's account identifier in the same form the request pipeline uses, so a command retried by
    /// a job is recognised as the same person's command; system work is identified by its job name.
    /// </summary>
    public string PrincipalId => IsSystem ? $"job:{Job.Name}" : UserId.ToString("n");

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public OrganisationContext Context { get; }

    /// <inheritdoc />
    public IReadOnlySet<Guid> AssignedBranches { get; }

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions { get; }

    /// <inheritdoc />
    /// <remarks>
    /// System work reports a satisfied second factor because there is no human factor to satisfy: the
    /// process is started by the deployment, and what it may do is bounded by its declared permissions
    /// rather than by a challenge. A job acting for a person reports what that person's session had
    /// satisfied when the job was queued, which is the only proof of a factor a queue can carry.
    /// </remarks>
    public bool MfaSatisfied { get; }

    /// <inheritdoc />
    public bool IsSignInComplete => true;

    /// <inheritdoc />
    /// <remarks>Always null: a job cannot re-authenticate, so it is never fresh.</remarks>
    public DateTimeOffset? LastReauthenticatedAt => null;

    /// <inheritdoc />
    public bool HasPermission(string permissionKey)
        => !string.IsNullOrEmpty(permissionKey) && Permissions.Contains(permissionKey);

    /// <inheritdoc />
    public bool CanActInBranch(Guid branchId)
        => branchId != Guid.Empty && (_reachesEveryBranch || AssignedBranches.Contains(branchId));

    internal static WorkerPrincipal System(WorkerJobDescriptor job, Guid? branchId)
    {
        var branches = branchId is { } branch && job.BranchScope == WorkerBranchScope.OneBranch
            ? new HashSet<Guid> { branch }
            : NoBranches;

        return new WorkerPrincipal(
            job,
            Guid.Empty,
            SystemAuditContext.SystemActor,
            new OrganisationContext(Guid.Empty, branchId),
            branches,
            PermissionsFor(job),
            job.BranchScope == WorkerBranchScope.Organisation,
            mfaSatisfied: true);
    }

    internal static WorkerPrincipal ForRequester(
        WorkerJobDescriptor job,
        RequesterAuthority authority,
        Guid? branchId,
        bool secondFactorSatisfied)
    {
        var branches = job.BranchScope switch
        {
            WorkerBranchScope.OneBranch when branchId is { } branch => new HashSet<Guid> { branch },
            WorkerBranchScope.AssignedBranches => authority.AssignedBranches.ToHashSet(),
            _ => NoBranches,
        };

        return new WorkerPrincipal(
            job,
            authority.UserId,
            authority.DisplayName,
            new OrganisationContext(authority.OrganisationId, branchId),
            branches,
            PermissionsFor(job),
            job.BranchScope == WorkerBranchScope.Organisation,
            secondFactorSatisfied);
    }

    /// <summary>
    /// The declared permissions, plus the organisation-wide read reach when the job declared that
    /// scope. Carrying the key as well as the reach is what keeps <see cref="HasPermission"/> and
    /// <see cref="CanActInBranch"/> from disagreeing: a job that may read every branch answers yes to
    /// both questions, and a job that may not answers no to both. For a job acting for a requester the
    /// key is added only after the requester has been shown to hold it.
    /// </summary>
    private static IReadOnlySet<string> PermissionsFor(WorkerJobDescriptor job)
    {
        if (job.BranchScope != WorkerBranchScope.Organisation)
        {
            return job.Permissions;
        }

        var permissions = job.Permissions.ToHashSet(StringComparer.Ordinal);
        permissions.Add(PlatformPermissions.ReadAllBranches);
        return permissions;
    }
}
