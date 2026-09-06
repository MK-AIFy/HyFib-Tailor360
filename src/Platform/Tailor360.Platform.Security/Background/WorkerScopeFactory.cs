using Microsoft.Extensions.DependencyInjection;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.Background;

/// <summary>
/// The only place a background job's principal is built. It reads the job's declaration, checks it
/// against the permission catalogue, re-reads the requester's authority when the job acts for someone,
/// and binds the resulting principal to a fresh dependency-injection scope.
/// </summary>
/// <param name="scopeFactory">Creates the dependency-injection scope the run uses.</param>
/// <param name="catalogue">The permission catalogue every declaration is checked against.</param>
public sealed class WorkerScopeFactory(IServiceScopeFactory scopeFactory, PermissionCatalogue catalogue)
    : IWorkerScopeFactory
{
    /// <inheritdoc />
    public IWorkerScope CreateSystemScope(Type jobType, Guid? branchId = null, string? correlationId = null)
    {
        var job = Describe(jobType);

        if (job.ActsForRequester)
        {
            throw new InvalidOperationException(
                $"Worker job '{job.Name}' is declared as acting for its requester, so it cannot run as "
                + "the system. Open the scope with CreateScopeForAsync, which re-reads the requester's "
                + "authority before the job runs.");
        }

        RequireConsistentBranch(job, branchId);

        return Open(scopeFactory.CreateScope(), WorkerPrincipal.System(job, branchId), correlationId);
    }

    /// <inheritdoc />
    public async Task<IWorkerScope> CreateScopeForAsync(
        Type jobType,
        WorkerJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = Describe(jobType);

        if (!job.ActsForRequester)
        {
            throw new InvalidOperationException(
                $"Worker job '{job.Name}' runs as the system, so it cannot be given a requester. A job "
                + "that acts for a person declares ActsForRequester, which is what makes its authority "
                + "be re-read rather than assumed.");
        }

        if (request.RequesterId == Guid.Empty)
        {
            throw new ArgumentException(
                "A job acting for a requester needs the requester's account identifier.", nameof(request));
        }

        RequireConsistentBranch(job, request.BranchId);

        var scope = scopeFactory.CreateScope();
        try
        {
            var authority = await scope.ServiceProvider
                .GetRequiredService<IRequesterAuthorityStore>()
                .FindAsync(request.RequesterId, cancellationToken);

            Authorise(job, request, authority);

            var principal = WorkerPrincipal.ForRequester(
                job, authority!, request.BranchId, request.SecondFactorSatisfied);

            return Open(scope, principal, request.CorrelationId);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private WorkerJobDescriptor Describe(Type jobType)
    {
        var job = WorkerJobDescriptor.For(jobType);
        job.Validate(catalogue);
        return job;
    }

    private static void RequireConsistentBranch(WorkerJobDescriptor job, Guid? branchId)
    {
        var hasBranch = branchId is { } branch && branch != Guid.Empty;

        if (job.BranchScope == WorkerBranchScope.OneBranch && !hasBranch)
        {
            throw new InvalidOperationException(
                $"Worker job '{job.Name}' runs for one branch and was given none. A branch-scoped job "
                + "with no branch would reach every branch or none, and neither is what was declared.");
        }

        if (hasBranch && job.BranchScope is WorkerBranchScope.None or WorkerBranchScope.Organisation)
        {
            throw new InvalidOperationException(
                $"Worker job '{job.Name}' declares the branch scope {job.BranchScope} and was given a "
                + "branch. A principal must not carry a branch it has no declared reason to act in.");
        }
    }

    private void Authorise(WorkerJobDescriptor job, WorkerJobRequest request, RequesterAuthority? authority)
    {
        if (authority is null)
        {
            throw Refuse(job, request, WorkerJobRefusal.UnknownRequester);
        }

        if (!authority.IsActive)
        {
            throw Refuse(job, request, WorkerJobRefusal.RequesterNotActive);
        }

        foreach (var key in job.Permissions)
        {
            RequireStillHeld(job, request, authority, key, WorkerJobRefusal.PermissionNotHeld);
        }

        switch (job.BranchScope)
        {
            case WorkerBranchScope.OneBranch when !authority.AssignedBranches.Contains(request.BranchId!.Value):
                throw Refuse(job, request, WorkerJobRefusal.BranchNotAssigned);

            case WorkerBranchScope.AssignedBranches when authority.AssignedBranches.Count == 0:
                throw Refuse(job, request, WorkerJobRefusal.NoBranchAssigned);

            case WorkerBranchScope.AssignedBranches
                when request.BranchId is { } branch && !authority.AssignedBranches.Contains(branch):
                throw Refuse(job, request, WorkerJobRefusal.BranchNotAssigned);

            case WorkerBranchScope.Organisation:
                // Reaching every branch is itself a permission, and the principal will carry it, so it
                // is checked exactly as a declared one would be rather than being implied by the scope.
                RequireStillHeld(
                    job,
                    request,
                    authority,
                    PlatformPermissions.ReadAllBranches,
                    WorkerJobRefusal.OrganisationReachNotHeld);
                break;

            default:
                break;
        }
    }

    private void RequireStillHeld(
        WorkerJobDescriptor job,
        WorkerJobRequest request,
        RequesterAuthority authority,
        string key,
        WorkerJobRefusal refusalWhenMissing)
    {
        if (!authority.Permissions.Contains(key))
        {
            throw Refuse(job, request, refusalWhenMissing);
        }

        if (!request.SecondFactorSatisfied && catalogue.Find(key) is { RequiresMfa: true })
        {
            throw Refuse(job, request, WorkerJobRefusal.SecondFactorNotSatisfied);
        }
    }

    private static WorkerJobAuthorisationException Refuse(
        WorkerJobDescriptor job,
        WorkerJobRequest request,
        WorkerJobRefusal reason)
        => new(job.Name, request.RequesterId, reason);

    private static WorkerScope Open(IServiceScope scope, WorkerPrincipal principal, string? correlationId)
    {
        scope.ServiceProvider.GetRequiredService<WorkerPrincipalAccessor>().Bind(principal, correlationId);
        return new WorkerScope(scope, principal, correlationId);
    }

    private sealed class WorkerScope(IServiceScope scope, WorkerPrincipal principal, string? correlationId)
        : IWorkerScope
    {
        public IServiceProvider Services => scope.ServiceProvider;

        public WorkerPrincipal Principal => principal;

        public WorkerJobDescriptor Job => principal.Job;

        public string? CorrelationId => correlationId;

        public void Dispose() => scope.Dispose();
    }
}
