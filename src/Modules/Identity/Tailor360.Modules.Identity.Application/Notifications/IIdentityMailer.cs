using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Application.Notifications;

/// <summary>
/// The messages this module sends. Four of them, named rather than composed, so that what a staff
/// member can receive from Tailor 360 is a list somebody can read.
/// </summary>
/// <remarks>
/// Every method returns <see langword="void"/> and none of them is awaited, because none of them may
/// happen on the request path: a recovery request has to take the same time whether or not the address
/// is known, and waiting for a mail relay would make the response time say which it was. The methods
/// render and enqueue; delivery happens afterwards.
/// <para>
/// Two rules hold for every message here. A message never contains a password, a recovery code or a
/// multi-factor secret — a recovery message carries a link and nothing else. And nothing about a
/// message reaches a log: not the body, not the recipient address, not the token in the link.
/// </para>
/// </remarks>
public interface IIdentityMailer
{
    /// <summary>Sends the link that lets someone set a new password.</summary>
    /// <param name="user">The account being recovered.</param>
    /// <param name="tokenValue">The single-use token. Goes in the link and nowhere else.</param>
    /// <param name="validFor">How long the link works for, which the message states in minutes.</param>
    void SendPasswordRecovery(StaffUser user, string tokenValue, TimeSpan validFor);

    /// <summary>Sends the link that lets an invited person set their first password (#25).</summary>
    /// <param name="user">The invited account.</param>
    /// <param name="tokenValue">The single-use token. Goes in the link and nowhere else.</param>
    /// <param name="validFor">How long the link works for, which the message states in minutes.</param>
    void SendInvitation(StaffUser user, string tokenValue, TimeSpan validFor);

    /// <summary>
    /// Tells someone their password has changed. This is the message that turns a silent account
    /// takeover into one the holder finds out about, so it is sent on every change, including the ones
    /// the holder made themselves.
    /// </summary>
    void SendPasswordChangedAlert(StaffUser user, DateTimeOffset changedAt);

    /// <summary>
    /// Tells someone an administrator has reset their second factor. Required by #23 because an
    /// administrative reset is the one path that removes a factor without the holder's involvement.
    /// </summary>
    void SendMfaResetAlert(StaffUser user, DateTimeOffset resetAt);
}
