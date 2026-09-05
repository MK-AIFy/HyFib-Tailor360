using Microsoft.Extensions.Logging;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Modules.Identity.Domain.Sessions;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Application.Authentication;

/// <summary>Ending sessions: this one, all of them, or a named one from the holder's own inventory.</summary>
/// <remarks>
/// Revoking a named session checks that it belongs to the caller before revoking it. The session
/// service takes an identifier and does as it is told, which is right for a service the administration
/// screens will also call; the ownership question belongs here, where the caller is known. Without it,
/// a signed-in person could end anyone's session by guessing an identifier — the textbook insecure
/// direct object reference, and one that would read as an outage rather than as an attack.
/// </remarks>
/// <param name="sessions">Session lifecycle.</param>
/// <param name="directory">Account lookup, for forgetting remembered devices.</param>
/// <param name="clock">The clock.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="logger">Logger.</param>
public sealed class SignOutHandler(
    ISessionService sessions,
    ISignInDirectory directory,
    Tailor360.Platform.Abstractions.Time.IClock clock,
    IAuditWriter audit,
    ILogger<SignOutHandler> logger)
{
    /// <summary>The audit action recorded when a session ends at its holder's request.</summary>
    public const string SignedOutAction = "identity.session.signed-out";

    /// <summary>The audit action recorded when every session on an account is ended.</summary>
    public const string SignedOutEverywhereAction = "identity.session.signed-out-everywhere";

    /// <summary>The audit action recorded when one session is revoked from the inventory.</summary>
    public const string SessionRevokedAction = "identity.session.revoked";

    /// <summary>Ends the session the request is being made under.</summary>
    public async Task<Result> SignOutAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var revoked = await sessions.RevokeAsync(sessionId, SessionEndReason.SignedOut, cancellationToken);
        if (revoked.IsFailure)
        {
            return revoked;
        }

        AuthenticationLog.SignedOut(logger, userId, 1);

        await AuthenticationAudit.RecordAsync(
            audit, SignedOutAction, userId, "Signed out.", cancellationToken, new { SessionId = sessionId });

        return Result.Success();
    }

    /// <summary>
    /// Ends every session on the account, including the one making the request, and forgets every
    /// remembered device.
    /// </summary>
    /// <remarks>
    /// The remembered devices go with the sessions deliberately. This is the control a person reaches
    /// for when they believe somebody else has their password; leaving a remembered device behind would
    /// mean that person's next sign-in from it needed only the password they are trying to get away
    /// from. Ending the sessions and leaving the bypass in place would be worse than doing nothing,
    /// because it looks like it worked.
    /// </remarks>
    public async Task<Result<int>> SignOutEverywhereAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var forgotten = await ForgetRememberedDevicesAsync(userId, cancellationToken);

        var revoked = await sessions.RevokeAllForUserAsync(
            userId, SessionEndReason.SignedOutEverywhere, cancellationToken: cancellationToken);

        if (revoked.IsFailure)
        {
            return revoked;
        }

        AuthenticationLog.SignedOut(logger, userId, revoked.Value);

        await AuthenticationAudit.RecordAsync(
            audit,
            SignedOutEverywhereAction,
            userId,
            $"Signed out of every device; {revoked.Value} session(s) ended and {forgotten} remembered "
            + "device(s) forgotten.",
            cancellationToken,
            new { SessionsEnded = revoked.Value, DevicesForgotten = forgotten });

        return revoked;
    }

    private async Task<int> ForgetRememberedDevicesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await directory.FindAsync(userId, cancellationToken);
        if (user is null)
        {
            return 0;
        }

        var now = clock.UtcNow;
        var usable = user.TrustedDevices.Where(device => device.IsUsable(now)).Select(device => device.Id);
        var forgotten = 0;

        foreach (var deviceId in usable.ToArray())
        {
            if (user.ForgetDevice(deviceId, now).IsSuccess)
            {
                forgotten++;
            }
        }

        return forgotten;
    }

    /// <summary>Ends one session named from the holder's own inventory.</summary>
    public async Task<Result> RevokeAsync(
        Guid userId,
        Guid targetSessionId,
        Guid? currentSessionId,
        CancellationToken cancellationToken = default)
    {
        var inventory = await sessions.ListForUserAsync(userId, currentSessionId, cancellationToken);

        if (!inventory.Any(session => session.SessionId == targetSessionId))
        {
            // "Not yours" and "does not exist" answer identically, so the inventory cannot be used to
            // discover which identifiers are live sessions on other people's accounts.
            return Result.Failure(IdentityErrors.SessionNotActive);
        }

        var revoked = await sessions.RevokeAsync(
            targetSessionId, SessionEndReason.RevokedByHolder, cancellationToken);

        if (revoked.IsFailure)
        {
            return revoked;
        }

        await AuthenticationAudit.RecordAsync(
            audit,
            SessionRevokedAction,
            userId,
            "A session was revoked from the holder's own device inventory.",
            cancellationToken,
            new { SessionId = targetSessionId });

        return Result.Success();
    }
}
