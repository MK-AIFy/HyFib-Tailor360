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

    /// <summary>
    /// Stops an account temporarily and ends every session it holds.
    /// </summary>
    /// <param name="userId">The account to suspend.</param>
    /// <param name="reason">Why, as the administrator typed it. Recorded in the audit trail.</param>
    /// <param name="actor">The administrator, for the refusal below.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredUser>> SuspendAsync(
        Guid userId,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        // An administrator suspending themselves would end their own sessions in the same request and
        // then be unable to sign in and undo it. Refusing is not paternalism: the account that can lift
        // a suspension is the one being suspended, so this is a door that locks from the outside only.
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

        var suspended = user.Suspend(clock.UtcNow, actor);
        if (suspended.IsFailure)
        {
            return Result.Failure<AdministeredUser>(suspended.Error);
        }

        var written = await store.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<AdministeredUser>(written.Error);
        }

        // Ended after the status is committed, so a failure here leaves a suspended account with live
        // sessions that the store's own repair will close on their next request — rather than an active
        // account whose sessions were ended for no recorded reason.
        var ended = await sessions.RevokeAllForUserAsync(
            userId, SessionEndReason.RevokedByAdministrator, exceptSessionId: null, cancellationToken);

        await AdministrationAudit.RecordAsync(
            audit,
            SuspendedAction,
            AdministrationAudit.StaffUserEntity,
            userId,
            $"The account was suspended and {ended.Value} session(s) were ended.",
            reason,
            before,
            SnapshotOf(user),
            cancellationToken);

        IdentityLog.AccountSuspended(logger, userId, ended.Value);

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
