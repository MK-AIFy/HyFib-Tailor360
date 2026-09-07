using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Security.Background;

namespace Tailor360.Worker.Jobs;

/// <summary>
/// Who is acting, for the audit trail, inside a background job's scope. It is composed in the host for
/// the same reason the web host composes its own: the security model supplies the principal and
/// neither it nor the persistence layer knows about the other.
/// </summary>
/// <remarks>
/// Without this every row a job writes would be attributed to <c>system</c> with no correlation, which
/// makes a change made by a job indistinguishable from a change made by a migration. A job acting for a
/// person is attributed to that person — they asked for the work — and system work is attributed to the
/// job by name, so "who dispatched this?" has an answer that names the dispatcher rather than the host.
/// A scope with no principal bound still falls back to the system actor, which is correct: nobody was
/// acting.
/// </remarks>
/// <param name="accessor">The principal bound to this scope.</param>
public sealed class WorkerAuditContext(WorkerPrincipalAccessor accessor) : IAuditContext
{
    /// <inheritdoc />
    public Guid? ActorId
        => accessor.Principal is WorkerPrincipal { IsSystem: false } principal ? principal.UserId : null;

    /// <inheritdoc />
    public string ActorDisplayName => accessor.Principal is WorkerPrincipal principal
        ? principal.IsSystem ? principal.Job.Name : principal.DisplayName
        : SystemAuditContext.SystemActor;

    /// <inheritdoc />
    public Guid? BranchId => accessor.Principal.Context.BranchId;

    /// <inheritdoc />
    public string? CorrelationId => accessor.CorrelationId;
}
