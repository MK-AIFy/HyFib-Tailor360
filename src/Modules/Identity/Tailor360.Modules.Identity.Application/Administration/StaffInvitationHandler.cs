using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Notifications;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Recovery;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Administration;

/// <summary>
/// Inviting somebody to work in the shop.
/// </summary>
/// <remarks>
/// <para>
/// An invitation creates an account that cannot yet sign in and a single-use link that lets its holder
/// set a password. It deliberately does not create a password: an administrator who could set one
/// would know a credential belonging to somebody else, and every later action taken with it would be
/// attributable to a person who never chose it.
/// </para>
/// <para>
/// The link is the same mechanism as password recovery, with a different purpose recorded against it,
/// rather than a second single-use-token scheme. One mechanism means one expiry rule, one revocation
/// path and one place where a mistake in either would be found.
/// </para>
/// </remarks>
/// <param name="store">Account reads and writes.</param>
/// <param name="tokens">Issues the single-use token.</param>
/// <param name="mailer">Sends the invitation. Never awaited on the request path.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="options">Recovery configuration, which owns the link's lifetime.</param>
/// <param name="logger">Logger. Never receives a name, an address or a token.</param>
public sealed class StaffInvitationHandler(
    IIdentityStore store,
    IRecoveryTokenService tokens,
    IIdentityMailer mailer,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    IOptions<RecoveryOptions> options,
    ILogger<StaffInvitationHandler> logger)
{
    /// <summary>The audit action recorded when an account is invited.</summary>
    public const string InvitedAction = "identity.user.invited";

    private readonly RecoveryOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Creates an invited account and sends its invitation.</summary>
    /// <param name="request">Who is being invited.</param>
    /// <param name="reason">Why, as the administrator typed it.</param>
    /// <param name="actor">The administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<AdministeredUser>> InviteAsync(
        InviteStaffMember request,
        string reason,
        Guid actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userName = request.UserName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;

        if (await store.IsIdentifierTakenAsync(
                request.OrganisationId, userName, email.ToUpperInvariant(), cancellationToken))
        {
            // One answer for both, deliberately. Telling an administrator which of the two collided is
            // useful; telling them is also telling anyone who reaches this endpoint whether a given
            // address is already a member of staff, and the endpoint cannot distinguish the two.
            return Result.Failure<AdministeredUser>(IdentityErrors.IdentifierAlreadyTaken);
        }

        var now = clock.UtcNow;

        var invited = StaffUser.Invite(
            ids.NewId(),
            request.OrganisationId,
            userName,
            email,
            request.DisplayName?.Trim(),
            now,
            request.HomeBranchId);

        if (invited.IsFailure)
        {
            return Result.Failure<AdministeredUser>(invited.Error);
        }

        var user = invited.Value;
        var minted = tokens.Issue();

        var token = RecoveryToken.Issue(
            ids.NewId(), user.Id, minted.TokenHash, RecoveryPurpose.Invitation, now, _options.TokenLifetime);

        if (token.IsFailure)
        {
            return Result.Failure<AdministeredUser>(token.Error);
        }

        store.AddUser(user);
        store.AddRecoveryToken(token.Value);

        var written = await store.TrySaveChangesAsync(cancellationToken);
        if (written.IsFailure)
        {
            return Result.Failure<AdministeredUser>(written.Error);
        }

        // Enqueued after the account exists, so a message can never name an account that was not
        // created — and never awaited, because a mail relay's latency is not the caller's to wait for.
        mailer.SendInvitation(user, minted.Value, _options.TokenLifetime);

        await AdministrationAudit.RecordAsync(
            audit,
            InvitedAction,
            AdministrationAudit.StaffUserEntity,
            user.Id,
            "An account was invited and its invitation was sent.",
            reason,
            before: null,
            after: new StaffUserSnapshot(
                user.Status.ToString(), user.MfaEnrolment.ToString(), user.HomeBranchId),
            cancellationToken);

        IdentityLog.AccountAdministered(logger, user.Id, InvitedAction, 0);

        return Result.Success(new AdministeredUser(
            user.Id,
            user.DisplayName,
            user.UserName,
            user.Email,
            user.Status,
            user.MfaEnrolment,
            user.HomeBranchId,
            user.LastSignInAt,
            user.CreatedAt,
            store.EntityTagOf(user)));
    }
}

/// <summary>Who is being invited.</summary>
/// <param name="OrganisationId">The organisation, taken from the caller rather than from the request.</param>
/// <param name="UserName">The sign-in name they will use.</param>
/// <param name="Email">Where the invitation is sent.</param>
/// <param name="DisplayName">The name colleagues will see.</param>
/// <param name="HomeBranchId">The branch they usually work in, if it is known yet.</param>
public sealed record InviteStaffMember(
    Guid OrganisationId,
    string? UserName,
    string? Email,
    string? DisplayName,
    Guid? HomeBranchId);
