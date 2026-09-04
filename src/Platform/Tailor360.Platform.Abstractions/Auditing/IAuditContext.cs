namespace Tailor360.Platform.Abstractions.Auditing;

/// <summary>
/// Who is acting, for the audit trail. Kept separate from the security model so that the persistence
/// layer can write audit entries without depending on the authentication implementation, and so that a
/// background job can supply a system or operator identity through the same interface.
/// </summary>
public interface IAuditContext
{
    /// <summary>The acting user, or null for a system action.</summary>
    Guid? ActorId { get; }

    /// <summary>The actor's display name as it was at the time of the action.</summary>
    string ActorDisplayName { get; }

    /// <summary>The branch the action belongs to, when it is branch-scoped.</summary>
    Guid? BranchId { get; }

    /// <summary>The correlation identifier of the request or job that caused the action.</summary>
    string? CorrelationId { get; }
}

/// <summary>
/// The audit context used when no user is acting: migrations, scheduled jobs and operator commands run
/// from the console. Naming the actor "system" rather than leaving it blank keeps every entry attributable.
/// </summary>
public sealed class SystemAuditContext : IAuditContext
{
    /// <summary>The display name used for system actions.</summary>
    public const string SystemActor = "system";

    /// <inheritdoc />
    public Guid? ActorId => null;

    /// <inheritdoc />
    public string ActorDisplayName => SystemActor;

    /// <inheritdoc />
    public Guid? BranchId => null;

    /// <inheritdoc />
    public string? CorrelationId => null;
}
