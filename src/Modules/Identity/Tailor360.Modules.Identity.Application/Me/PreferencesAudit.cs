using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Identity.Application.Me;

/// <summary>
/// Writes the preferences-change audit entry and commits it.
/// </summary>
/// <remarks>
/// Committed on its own rather than with the change it describes, for the same reason
/// <see cref="Authentication.AuthenticationAudit"/> is: the change is in the <c>identity</c> schema and
/// the trail is in <c>platform</c>, different contexts and therefore different transactions, so the
/// caller saves its own change first and this records afterwards — the trail can only ever lag reality,
/// never lead it. What may go in the entry is the actor and the resource, and nothing else: what a
/// preference was set to is never in the row, which is what lets an administrator answering "why does
/// this person see the interface in Tamil" read the trail without it becoming a second copy of the
/// preference itself.
/// </remarks>
internal static class PreferencesAudit
{
    /// <summary>The entity type a preferences-change entry is recorded against.</summary>
    public const string EntityType = "StaffUser";

    /// <summary>Records that the caller replaced their own preferences, and commits the entry.</summary>
    /// <param name="audit">The audit trail.</param>
    /// <param name="userId">The account whose preferences changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task RecordAsync(IAuditWriter audit, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);

        await audit.WriteAsync(
            new AuditEntry(
                PreferencesHandler.ChangedAction,
                EntityType,
                userId,
                "Interface preferences replaced.",
                Reason: null,
                Before: null,
                After: null),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}
