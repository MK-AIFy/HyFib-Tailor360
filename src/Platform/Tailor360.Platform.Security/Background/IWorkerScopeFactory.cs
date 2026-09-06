using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Platform.Security.Background;

/// <summary>
/// Opens the scope a background job runs in: a dependency-injection scope with a principal bound to
/// it, built from the job's <see cref="WorkerJobAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only the worker and the command-line tool may use this, and an architecture test says so. A job is
/// the one place in the system where a principal is chosen rather than presented, so the choice is
/// confined to the two hosts that genuinely have no request behind them. Module code that reached for
/// it would be selecting its own authority.
/// </para>
/// <para>
/// The scope owns the principal for its whole life: <see cref="ICurrentUser"/> resolved from
/// <see cref="IWorkerScope.Services"/> is the job's principal, so every existing authorisation check,
/// audit entry and branch filter behaves the same way in a job as it does in a request.
/// </para>
/// </remarks>
public interface IWorkerScopeFactory
{
    /// <summary>
    /// Opens a scope for a job that runs on its own declared authority.
    /// </summary>
    /// <param name="jobType">The job type, which must carry a <see cref="WorkerJobAttribute"/>.</param>
    /// <param name="branchId">
    /// The branch the run is for. Required when the job declares
    /// <see cref="WorkerBranchScope.OneBranch"/> and refused otherwise, so a branch is never carried by
    /// a principal that has no business acting in one.
    /// </param>
    /// <param name="correlationId">
    /// The correlation identifier of the work that caused this run, so a user action and the job it
    /// triggered share one identifier. Null when the run was caused by a timer.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The job carries no declaration, declares a permission no module owns, declares the branch scope
    /// of a requester, or is declared as acting for a requester.
    /// </exception>
    IWorkerScope CreateSystemScope(Type jobType, Guid? branchId = null, string? correlationId = null);

    /// <summary>
    /// Opens a scope for a job that acts on behalf of the person who queued it, after re-reading that
    /// person's authority.
    /// </summary>
    /// <param name="jobType">The job type, which must carry a <see cref="WorkerJobAttribute"/>.</param>
    /// <param name="request">Who queued the job, for which branch, and what their session had proved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// The job carries no declaration, declares a permission no module owns, declares a step-up
    /// permission, or is not declared as acting for a requester.
    /// </exception>
    /// <exception cref="WorkerJobAuthorisationException">
    /// The requester no longer holds what the job needs. The job is aborted, not retried.
    /// </exception>
    Task<IWorkerScope> CreateScopeForAsync(
        Type jobType,
        WorkerJobRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A background job's scope: the services it resolves from and the principal it runs as, disposed
/// together when the run ends.
/// </summary>
public interface IWorkerScope : IDisposable
{
    /// <summary>The services for this run. <see cref="ICurrentUser"/> resolves to <see cref="Principal"/>.</summary>
    IServiceProvider Services { get; }

    /// <summary>The principal this run acts as.</summary>
    WorkerPrincipal Principal { get; }

    /// <summary>The declaration the principal was built from.</summary>
    WorkerJobDescriptor Job { get; }

    /// <summary>The correlation identifier for this run, or null when nothing caused it but a timer.</summary>
    string? CorrelationId { get; }
}

/// <summary>
/// What a queued job records about the person who queued it. Deliberately small: an identifier, a
/// branch and the proof of a factor. Everything else — roles, permissions, branch assignments — is read
/// again when the job runs, because a queue that carried permissions would carry stale ones.
/// </summary>
/// <param name="RequesterId">The account that queued the job.</param>
/// <param name="BranchId">The branch the work is for, when the job is branch-scoped.</param>
/// <param name="SecondFactorSatisfied">
/// True when the session that queued the job had completed a multi-factor challenge. It is the only
/// assurance a queue can carry, and it is a ceiling rather than a grant: it lets a job exercise a
/// permission that demands a second factor, and it never adds a permission the requester has since lost.
/// </param>
/// <param name="CorrelationId">The correlation identifier of the request that queued the job.</param>
public sealed record WorkerJobRequest(
    Guid RequesterId,
    Guid? BranchId = null,
    bool SecondFactorSatisfied = false,
    string? CorrelationId = null);
