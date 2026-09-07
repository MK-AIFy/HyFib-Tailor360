using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Identity.Application.Administration;

/// <summary>
/// Writes the administrative surface's audit entries and commits them.
/// </summary>
/// <remarks>
/// <para>
/// Every administrative change is a change one person made to another person's access, so the entry has
/// to answer four questions a reviewer will actually ask: who did it, to whom, what changed, and why.
/// The first two come from the request's audit context and the subject identifier; the last two are
/// this type's reason for existing, because the sign-in path's writer carries neither a reason nor a
/// prior state and an entry without them cannot answer "what was it before".
/// </para>
/// <para>
/// <b>What may go in a snapshot.</b> The administered fields, as identifiers rather than names: a status
/// as its name, a role as its key, a branch as its identifier, a flag as its value. Never a sign-in
/// name, an address, a phone number, a password, a code or a token. An audit entry is read by more
/// people than a log line is and is retained for years, so the rule here is stricter than the one for
/// logs, not looser.
/// </para>
/// <para>
/// The entry is committed on its own, after the change it describes, for the reason written out in
/// <c>AuthenticationAudit</c>: the change is in the <c>identity</c> schema and the trail is in
/// <c>platform</c>, which are different contexts and so different transactions. The trail can therefore
/// lag reality but never lead it, and closing that gap is a platform change owned by #21 and #57.
/// </para>
/// </remarks>
internal static class AdministrationAudit
{
    /// <summary>The entity type an entry about an account is recorded against.</summary>
    public const string StaffUserEntity = "StaffUser";

    /// <summary>Records one administrative entry and commits it.</summary>
    /// <param name="audit">The audit trail.</param>
    /// <param name="action">The stable action name, for example <c>identity.user.suspended</c>.</param>
    /// <param name="entityType">The kind of thing administered.</param>
    /// <param name="entityId">Which one.</param>
    /// <param name="summary">What happened, in words an operator can read.</param>
    /// <param name="reason">The reason the administrator gave. Required for every action here.</param>
    /// <param name="before">Redacted prior state, so the entry says what changed rather than only what it is now.</param>
    /// <param name="after">Redacted resulting state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task RecordAsync(
        IAuditWriter audit,
        string action,
        string entityType,
        Guid entityId,
        string summary,
        string reason,
        object? before,
        object? after,
        CancellationToken cancellationToken)
    {
        await audit.WriteAsync(
            new AuditEntry(action, entityType, entityId, summary, reason, before, after),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// The administered state of one account, as it is recorded in the audit trail before and after a
/// change.
/// </summary>
/// <remarks>
/// A record rather than the aggregate itself, so that what is written down is a deliberate choice
/// instead of whatever the entity happens to expose. Adding a field here puts it in the trail for
/// years; that should take an edit and a moment's thought.
/// </remarks>
/// <param name="Status">The account status, as its name.</param>
/// <param name="MfaEnrolment">Whether a second factor is enrolled, as its name.</param>
/// <param name="HomeBranchId">The account's default branch, as an identifier.</param>
internal sealed record StaffUserSnapshot(string Status, string MfaEnrolment, Guid? HomeBranchId);
