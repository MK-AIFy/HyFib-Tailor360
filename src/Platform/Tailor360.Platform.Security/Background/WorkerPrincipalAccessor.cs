using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Platform.Security.Background;

/// <summary>
/// Holds the principal a worker scope was opened with, so that <see cref="ICurrentUser"/> resolved
/// anywhere inside that scope is the job's principal.
/// </summary>
/// <remarks>
/// It is the worker's equivalent of the session the authentication handler fills in for a request,
/// and it fails closed the same way: a scope nobody bound a principal to reports the anonymous caller,
/// which holds nothing and can act in no branch. That is what makes an ordinary
/// <c>IServiceScopeFactory.CreateScope()</c> in the worker — the outbox dispatcher's, for instance —
/// safe to leave as it is rather than quietly privileged.
/// </remarks>
public sealed class WorkerPrincipalAccessor
{
    private static readonly AnonymousCurrentUser Nobody = new();

    /// <summary>The principal bound to this scope, or the anonymous caller when none was bound.</summary>
    public ICurrentUser Principal { get; private set; } = Nobody;

    /// <summary>The correlation identifier of the run, or null.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>The job this scope was opened for, or null when the scope is not a job's.</summary>
    public WorkerJobDescriptor? Job { get; private set; }

    internal void Bind(WorkerPrincipal principal, string? correlationId)
    {
        if (Job is not null)
        {
            throw new InvalidOperationException(
                "This scope already runs as a principal. A scope carries one principal for its whole "
                + "life; a job that needs a different one opens its own scope.");
        }

        Principal = principal;
        Job = principal.Job;
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId;
    }
}
