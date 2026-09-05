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
}
