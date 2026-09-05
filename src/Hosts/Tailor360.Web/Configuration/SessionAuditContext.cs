using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Observability.Correlation;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Web.Configuration;

/// <summary>
/// Who is acting, for the audit trail, on a request that carries a session. It is composed in the host
/// because the host is the only place that knows both halves: the security model supplies the caller,
/// the observability model supplies the correlation identifier, and neither project references the
/// other.
/// </summary>
/// <remarks>
/// Without this, every audit entry written during an authenticated request would be attributed to
/// <c>system</c> with no correlation identifier — an audit trail that records what happened but not who
/// did it, which is the half that matters. An unauthenticated request still falls back to the system
/// actor, which is correct: nobody was acting.
/// </remarks>
/// <param name="currentUser">The caller resolved from the session.</param>
/// <param name="correlation">The correlation identifier for this request.</param>
public sealed class SessionAuditContext(ICurrentUser currentUser, ICorrelationContext correlation)
    : IAuditContext
{
    /// <inheritdoc />
    public Guid? ActorId => currentUser.IsAuthenticated ? currentUser.UserId : null;

    /// <inheritdoc />
    public string ActorDisplayName
        => currentUser.IsAuthenticated ? currentUser.DisplayName : SystemAuditContext.SystemActor;

    /// <inheritdoc />
    public Guid? BranchId => currentUser.Context.BranchId;

    /// <inheritdoc />
    public string? CorrelationId
        => string.IsNullOrEmpty(correlation.CorrelationId) ? null : correlation.CorrelationId;
}
