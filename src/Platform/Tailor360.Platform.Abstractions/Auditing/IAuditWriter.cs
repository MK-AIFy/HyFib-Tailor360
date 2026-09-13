namespace Tailor360.Platform.Abstractions.Auditing;

/// <summary>
/// Writes audit entries. The implementation enlists in the caller's own unit of work — the same context,
/// the same change tracker, the same <c>SaveChangesAsync</c> call as the change the entry describes — so
/// an audit entry is committed with that change and rolled back with it. Application code depends on this
/// interface, never on the persistence implementation.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Stages an audited action against the current actor and correlation. Call this before the caller's
    /// own <c>SaveChangesAsync</c> (or the store method that wraps it), on a writer bound to the same
    /// context that call uses: that is what makes the entry ride the same save, and therefore the same
    /// transaction, as the change it describes (issue #179). A module gives itself its own binding of
    /// this — the way <c>Billing</c> has <c>IBillingAuditWriter</c> — for the same reason it has its own
    /// event-publisher port: the host composes every module at once, so one non-generic registration
    /// would resolve to whichever module's context happened to be bound last.
    /// </summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the entries recorded so far, for a caller with no unit of work of its own to ride — for
    /// example the default, non-generic binding of this interface, which writes through the platform
    /// context directly.
    /// </summary>
    /// <remarks>
    /// Prefer staging the entry on the caller's own context and letting it ride that context's own save:
    /// that is what makes the entry and the change it describes commit or roll back together. This exists
    /// for a caller with nothing of its own to attach to, where the alternative would be an audit trail
    /// with nothing in it. The two commits are then separate, so a caller must save its own change first
    /// and record the audit entry afterwards: an entry describing a change that did not happen is worse
    /// than a change that was not recorded, because the entry will be believed.
    /// </remarks>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
