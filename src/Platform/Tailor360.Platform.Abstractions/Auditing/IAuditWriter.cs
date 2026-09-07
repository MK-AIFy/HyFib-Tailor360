namespace Tailor360.Platform.Abstractions.Auditing;

/// <summary>
/// Writes audit entries. The implementation enlists in the caller's transaction, so an audit entry is
/// committed with the change it describes and rolled back with it. Application code depends on this
/// interface, never on the persistence implementation.
/// </summary>
public interface IAuditWriter
{
    /// <summary>Records an audited action against the current actor and correlation.</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the entries recorded so far, for a caller whose own change is committed through a
    /// different unit of work.
    /// </summary>
    /// <remarks>
    /// Prefer saving the caller's own context and letting the entry ride with it: that is what makes
    /// the entry and the change it describes commit or roll back together. This exists for the case
    /// where they genuinely cannot — a module whose data lives in its own schema and its own context,
    /// such as a sign-in writing to <c>identity</c> — where the alternative would be an audit trail
    /// with nothing in it. The two commits are then separate, so a caller must save its own change
    /// first and record the audit entry afterwards: an entry describing a change that did not happen
    /// is worse than a change that was not recorded, because the entry will be believed.
    /// </remarks>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
