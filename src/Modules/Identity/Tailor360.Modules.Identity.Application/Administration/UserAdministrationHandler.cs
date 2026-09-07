using Microsoft.Extensions.Logging;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Administration;

/// <summary>
/// Administering one staff account: reading it for an administrative screen, and changing its standing.
/// </summary>
/// <remarks>
/// <para>
/// The domain already knows which status transitions are legal and refuses the rest, so this type is
/// not where those rules live. What it owns is everything around them: the precondition the caller
/// edited against, the reason they gave, the sessions a suspension has to end, and the audit entry that
/// says what the account looked like before and after. Those are the parts that make an administrative
/// change reviewable, and none of them belongs in an aggregate.
/// </para>
/// <para>
/// Suspension ends the account's live sessions in the same request, deliberately rather than relying on
/// the session store's own repair. That repair — a session row is revoked the next time it is
/// presented — is a backstop for a request already in flight, and it leaves rows live until somebody
/// touches them. "Suspension revokes active sessions" has to mean the sessions are gone when the
/// administrator's request returns, or the administrator has no way to know whether it worked.
/// </para>
/// </remarks>
/// <param name="store">Account reads and writes.</param>
/// <param name="sessions">The session service, for ending an account's sessions.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger. Never receives a reason, a name or an address.</param>
public sealed class UserAdministrationHandler(
    IIdentityStore store,
    ISessionService sessions,
    IAuditWriter audit,
    IClock clock,
    ILogger<UserAdministrationHandler> logger)
{
    /// <summary>The audit action recorded when an account is suspended.</summary>
    public const string SuspendedAction = "identity.user.suspended";

    /// <summary>The audit action recorded when a suspension is lifted.</summary>
    public const string ReinstatedAction = "identity.user.reinstated";

    /// <summary>The audit action recorded when an account is closed.</summary>
    public const string DeactivatedAction = "identity.user.deactivated";

    /// <summary>The audit action recorded when a closed account is brought back.</summary>
    public const string ReactivatedAction = "identity.user.reactivated";

    /// <summary>The audit action recorded when an administrator clears a second factor.</summary>
    public const string MfaResetAction = "identity.user.mfa-reset";

    /// <summary>The audit action recorded when an administrator ends an account's sessions.</summary>
    public const string SessionsRevokedAction = "identity.user.sessions-revoked";

    /// <summary>Reads one account for an administrative screen, with the version it may be edited against.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredUser>> ReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);

        return user is null
            ? Result.Failure<AdministeredUser>(IdentityErrors.UserNotFound)
            : Result.Success(Describe(user, store.EntityTagOf(user)));
    }

    /// <summary>Stops an account temporarily and ends every session it holds.</summary>
    /// <param name="userId">The account to suspend.</param>
    /// <param name="reason">Why, as the administrator typed it. Recorded in the audit trail.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<AdministeredUser>> SuspendAsync(
        Guid userId,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            userId,
            reason,
            actor,
            (user, now) => user.Suspend(now, actor),
            SuspendedAction,
            ended => $"The account was suspended and {ended} session(s) were ended.",
            SessionEndReason.RevokedByAdministrator,
            cancellationToken);

    /// <summary>Lifts a suspension, so the account may sign in again.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<AdministeredUser>> ReinstateAsync(
        Guid userId,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            userId,
            reason,
            actor,
            (user, now) => user.Reinstate(now, actor),
            ReinstatedAction,
            _ => "The suspension was lifted.",
            endSessions: null,
            cancellationToken);

    /// <summary>
    /// Closes an account for good, ending its sessions and its remembered devices.
    /// </summary>
    /// <remarks>
    /// Nothing is deleted. Every "who did this" reference in the audit trail, every order taken at a
    /// counter and every invoice raised names an account, and those have to stay resolvable for years
    /// after the person has left — which is why there is no delete endpoint here and never will be.
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<AdministeredUser>> DeactivateAsync(
        Guid userId,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            userId,
            reason,
            actor,
            (user, now) => user.Deactivate(now, actor),
            DeactivatedAction,
            ended => $"The account was closed and {ended} session(s) were ended.",
            SessionEndReason.AccountClosed,
            cancellationToken);

    /// <summary>
    /// Brings a closed account back, as an invitation rather than as a working account.
    /// </summary>
    /// <remarks>
    /// The domain empties the password and the second factors, so what comes back is an account that
    /// has to prove itself again from the beginning. That is the point: the person returning has to be
    /// shown to be the person who left, and a reactivation that restored a working credential would
    /// turn a departed colleague's old password into a live one.
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<AdministeredUser>> ReactivateAsync(
        Guid userId,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            userId,
            reason,
            actor,
            (user, now) => user.Reactivate(now, actor, reason),
            ReactivatedAction,
            _ => "The account was reopened and must be invited again before it can be used.",
            endSessions: null,
            cancellationToken);

    /// <summary>
    /// Clears the account's second factor, after the administrator has identified the holder out of
    /// band.
    /// </summary>
    /// <remarks>
    /// This is the most abusable action on the surface — it is how somebody who has lost their phone
    /// gets back in, and therefore how somebody who has taken over an administrator's session gets into
    /// anybody's account. Ending the holder's sessions is part of the reset rather than a courtesy: an
    /// account whose factor was cleared by somebody else must not stay signed in on a device the real
    /// holder cannot see.
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<AdministeredUser>> ResetMfaAsync(
        Guid userId,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            userId,
            reason,
            actor,
            (user, now) => user.ResetMfa(now, actor, reason),
            MfaResetAction,
            ended => $"The second factor was cleared and {ended} session(s) were ended.",
            SessionEndReason.MfaReset,
            cancellationToken);

    /// <summary>
    /// Ends every session the account holds, without changing its standing.
    /// </summary>
    /// <remarks>
    /// The emergency lever: a phone left on a bus, a shared machine nobody signed out of. It is
    /// separate from suspension because the account is not in trouble — the person keeps their access
    /// and simply has to sign in again.
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Result<AdministeredUser>> RevokeSessionsAsync(
        Guid userId,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
        => ApplyAsync(
            userId,
            reason,
            actor,
            (_, _) => Result.Success(),
            SessionsRevokedAction,
            ended => $"{ended} session(s) were ended by an administrator.",
            SessionEndReason.RevokedByAdministrator,
            cancellationToken);

    /// <summary>
    /// The shape every administrative change to an account shares: refuse to act on your own account,
    /// read it, snapshot it, let the domain decide, save, end sessions if the change implies it, and
    /// record what changed and why.
    /// </summary>
    /// <remarks>
    /// One method rather than six near-copies, because the parts that must not vary are the parts that
    /// are easy to leave out of a copy: the self-administration guard, the before-image taken before
    /// the change rather than after it, and an audit entry that is written for every path that
    /// succeeded. A sixth command added later gets all three by construction.
    /// </remarks>
    private async Task<Result<AdministeredUser>> ApplyAsync(
        Guid userId,
        string reason,
        Guid actor,
        Func<StaffUser, DateTimeOffset, Result> change,
        string action,
        Func<int, string> summary,
        SessionEndReason? endSessions,
        CancellationToken cancellationToken)
    {
        if (userId == actor)
        {
            return Result.Failure<AdministeredUser>(IdentityErrors.CannotAdministerOwnAccount);
        }

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AdministeredUser>(IdentityErrors.UserNotFound);
        }

        var before = SnapshotOf(user);

        var changed = change(user, clock.UtcNow);
        if (changed.IsFailure)
        {
            return Result.Failure<AdministeredUser>(changed.Error);
        }

        var written = await store.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<AdministeredUser>(written.Error);
        }

        var ended = 0;

        if (endSessions is { } endReason)
        {
            // After the change is committed, so a failure here leaves the account in its new standing
            // with sessions the store's own repair will close on their next request — rather than an
            // unchanged account whose sessions were ended for no recorded reason.
            var revoked = await sessions.RevokeAllForUserAsync(
                userId, endReason, exceptSessionId: null, cancellationToken);

            ended = revoked.Value;
        }

        await AdministrationAudit.RecordAsync(
            audit,
            action,
            AdministrationAudit.StaffUserEntity,
            userId,
            summary(ended),
            reason,
            before,
            SnapshotOf(user),
            cancellationToken);

        IdentityLog.AccountAdministered(logger, userId, action, ended);

        return Result.Success(Describe(user, store.EntityTagOf(user)));
    }

    private static StaffUserSnapshot SnapshotOf(StaffUser user)
        => new(user.Status.ToString(), user.MfaEnrolment.ToString(), user.HomeBranchId);

    private static AdministeredUser Describe(StaffUser user, EntityTag version) => new(
        user.Id,
        user.DisplayName,
        user.UserName,
        user.Email,
        user.Status,
        user.MfaEnrolment,
        user.HomeBranchId,
        user.LastSignInAt,
        user.CreatedAt,
        version);
}

/// <summary>
/// One account as an administrative screen sees it, with the version an edit must be made against.
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The name shown on screen.</param>
/// <param name="UserName">The sign-in name. Personal data: it appears on the screen and nowhere else.</param>
/// <param name="Email">The address invitations and security alerts go to. Personal data, as above.</param>
/// <param name="Status">Whether the account is invited, active, suspended or deactivated.</param>
/// <param name="MfaEnrolment">Whether a second factor is enrolled, which an administrator may need to reset.</param>
/// <param name="HomeBranchId">The account's default branch.</param>
/// <param name="LastSignInAt">When the account was last used, which is what tells an administrator it is dormant.</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="Version">The concurrency token an edit must present.</param>
public sealed record AdministeredUser(
    Guid UserId,
    string DisplayName,
    string UserName,
    string Email,
    UserStatus Status,
    MfaEnrolmentState MfaEnrolment,
    Guid? HomeBranchId,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt,
    EntityTag Version);
