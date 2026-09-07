namespace Tailor360.Platform.Security.Authentication;

/// <summary>
/// The session resolved for the current request, set once by the authentication handler and read by
/// everything downstream. It exists so that <see cref="Authorisation.ICurrentUser"/> and the endpoints
/// do not have to re-read claims or re-query the store, and so that the pipeline has one place to look
/// when a request carries a cookie the server refused.
/// </summary>
/// <remarks>
/// Registered per request. It starts as "no ticket", which is the fail-closed state: code that runs
/// before authentication, or in a scope where authentication never ran, sees an unauthenticated caller
/// rather than a stale one.
/// </remarks>
public sealed class SessionContext
{
    /// <summary>Why the request is or is not carrying a usable session.</summary>
    public SessionTicketStatus Status { get; private set; } = SessionTicketStatus.NoTicket;

    /// <summary>The resolved session, or null when the request is not acting under one.</summary>
    public SessionTicket? Ticket { get; private set; }

    /// <summary>Identity of the current session, when there is one.</summary>
    public Guid? SessionId => Ticket?.SessionId;

    /// <summary>Records what the ticket store said about the presented cookie.</summary>
    public void Set(SessionResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        Status = resolution.Status;
        Ticket = resolution.IsActive ? resolution.Ticket : null;
    }
}
