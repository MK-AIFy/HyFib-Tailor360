using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Identity.Application.Authentication;

/// <summary>
/// Writes the sign-in path's audit entries and commits them.
/// </summary>
/// <remarks>
/// <para>
/// The entries are committed on their own rather than with the change they describe, because the
/// change is in the <c>identity</c> schema and the audit trail is in <c>platform</c>, which are
/// different contexts and therefore different transactions. Every call site therefore saves its own
/// change first and records afterwards, so the trail can only ever lag reality — never lead it.
/// Closing the gap properly means enlisting both contexts in one transaction, which is a platform
/// change owned by #21 and #57.
/// </para>
/// <para>
/// <b>What may go in an entry.</b> The account identifier, what happened, and how many times. Never a
/// sign-in name, an address, a password, a code, a session token or a device cookie: an audit entry is
/// read by more people than a log line is, and the trail is retained for years.
/// </para>
/// </remarks>
internal static class AuthenticationAudit
{
    /// <summary>The entity type every sign-in entry is recorded against.</summary>
    public const string EntityType = "StaffUser";

    /// <summary>Records one entry and commits it.</summary>
    /// <param name="audit">The audit trail.</param>
    /// <param name="action">The stable action name.</param>
    /// <param name="userId">The account the entry is about.</param>
    /// <param name="summary">What happened, in words an operator can read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="after">Redacted resulting state, or null.</param>
    /// <param name="actorId">
    /// Who acted, where the request's own audit context cannot know. On a sign-in nobody is
    /// authenticated when the request arrives — the session is created inside the handler — so an entry
    /// that deferred to the context would record every sign-in against <c>system</c> and leave it out of
    /// the actor index, which is where an investigation starts.
    /// </param>
    /// <param name="actorDisplayName">The actor's name, supplied with <paramref name="actorId"/>.</param>
    public static async Task RecordAsync(
        IAuditWriter audit,
        string action,
        Guid userId,
        string summary,
        CancellationToken cancellationToken,
        object? after = null,
        Guid? actorId = null,
        string? actorDisplayName = null)
    {
        await audit.WriteAsync(
            new AuditEntry(
                action,
                EntityType,
                userId,
                summary,
                Reason: null,
                Before: null,
                After: after,
                ActorId: actorId,
                ActorDisplayName: actorDisplayName),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}
