using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Platform.Security.Authentication;

namespace Tailor360.Modules.Identity.Api;

/// <summary>
/// Describes when a session ends, so the client can raise the warning dialog before it does rather
/// than discovering the expiry as a refusal in the middle of somebody's work.
/// </summary>
public static class SessionResponses
{
    /// <summary>Describes a session that has just been issued.</summary>
    public static SessionExpiryPayload Expiry(IssuedSession session, SessionAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);

        return new SessionExpiryPayload(
            session.IdleExpiresAt,
            session.AbsoluteExpiresAt,
            (int)options.ExpiryWarningLead.TotalSeconds,
            session.MfaSatisfied);
    }

    /// <summary>Describes the session the current request is being made under.</summary>
    public static SessionExpiryPayload Expiry(SessionTicket ticket, SessionAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(options);

        return new SessionExpiryPayload(
            ticket.IdleExpiresAt,
            ticket.AbsoluteExpiresAt,
            (int)options.ExpiryWarningLead.TotalSeconds,
            ticket.MfaSatisfied);
    }
}
