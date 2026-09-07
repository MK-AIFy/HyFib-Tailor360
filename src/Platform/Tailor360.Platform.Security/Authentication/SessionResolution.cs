namespace Tailor360.Platform.Security.Authentication;

/// <summary>The outcome of looking a presented session cookie up in the ticket store.</summary>
/// <param name="Status">Why the request is or is not carrying a usable session.</param>
/// <param name="Ticket">The session, present only when <paramref name="Status"/> is Active.</param>
public sealed record SessionResolution(SessionTicketStatus Status, SessionTicket? Ticket = null)
{
    /// <summary>No cookie was presented.</summary>
    public static SessionResolution NoTicket { get; } = new(SessionTicketStatus.NoTicket);

    /// <summary>A cookie was presented but names no session.</summary>
    public static SessionResolution Unknown { get; } = new(SessionTicketStatus.Unknown);

    /// <summary>The named session has been revoked.</summary>
    public static SessionResolution Revoked { get; } = new(SessionTicketStatus.Revoked);

    /// <summary>The named session has expired.</summary>
    public static SessionResolution Expired { get; } = new(SessionTicketStatus.Expired);

    /// <summary>The named session is live.</summary>
    public static SessionResolution Active(SessionTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return new SessionResolution(SessionTicketStatus.Active, ticket);
    }

    /// <summary>True when the request may act under this session.</summary>
    public bool IsActive => Status == SessionTicketStatus.Active && Ticket is not null;
}
