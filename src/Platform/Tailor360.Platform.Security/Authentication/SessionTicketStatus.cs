namespace Tailor360.Platform.Security.Authentication;

/// <summary>
/// Why a request is or is not carrying a usable session. The distinction between "no cookie" and
/// "a cookie the server will not honour" is what lets the client tell a first visit apart from a
/// session that ended underneath the person using it, which are different things to say to them.
/// </summary>
public enum SessionTicketStatus
{
    /// <summary>No session cookie was presented.</summary>
    NoTicket = 0,

    /// <summary>A cookie was presented but names no session. A stale, forged or already-deleted value.</summary>
    Unknown = 1,

    /// <summary>The session exists and was revoked. Signing out elsewhere, or an administrator ending it.</summary>
    Revoked = 2,

    /// <summary>The session exists and has passed its inactivity or absolute deadline.</summary>
    Expired = 3,

    /// <summary>The session is live and the request may act under it.</summary>
    Active = 4,
}
