using Microsoft.AspNetCore.Http;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Modules.Identity.Api;

/// <summary>
/// The few facts about a request the sign-in path needs, read in one place so that no endpoint invents
/// its own idea of what the client address is.
/// </summary>
public static class RequestFacts
{
    /// <summary>The longest user agent string kept, which is well past any real one.</summary>
    private const int MaximumUserAgentLength = 400;

    /// <summary>
    /// The client address, already resolved through the trusted reverse proxy by the forwarded-headers
    /// configuration. Null when there is none, which the throttle treats as "no address to count".
    /// </summary>
    public static string? ClientAddress(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>The user agent, truncated, for the holder's own device inventory.</summary>
    public static string? UserAgent(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var agent = context.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(agent))
        {
            return null;
        }

        return agent.Length > MaximumUserAgentLength ? agent[..MaximumUserAgentLength] : agent;
    }

    /// <summary>The session the request is being made under, when it carries one.</summary>
    public static Guid? SessionId(SessionContext session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.SessionId;
    }

    /// <summary>
    /// The session a self-service request is being made under, assembled in one place so that no
    /// endpoint can hand a handler an account identifier without the evidence that goes with it.
    /// </summary>
    /// <remarks>
    /// Every field comes from the ticket the authentication handler resolved on this request, never from
    /// the body. Assembling it here rather than at each call site is deliberate: the handlers behind
    /// these endpoints refuse a caller who has not satisfied a second factor, and a call site that
    /// passed only a user identifier would compile and quietly skip that refusal.
    /// </remarks>
    /// <param name="caller">The caller resolved from the session.</param>
    /// <param name="session">The session resolved for this request.</param>
    public static CallerSession Caller(ICurrentUser caller, SessionContext session)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(session);

        return new CallerSession(caller.UserId, session.SessionId, caller.MfaSatisfied);
    }

    /// <summary>
    /// A short, human label for the device, derived from the user agent.
    /// </summary>
    /// <remarks>
    /// Derived on the server rather than accepted from the client. The label is shown to the holder in
    /// their session inventory beside a <b>Revoke</b> control, and a label the client chose could be
    /// made to read "This device" on somebody else's phone — which is precisely the row a person would
    /// not revoke. It is a rough guess by design: it only has to let someone tell their own two devices
    /// apart, and a wrong guess is a cosmetic problem where a spoofed one is a security problem.
    /// </remarks>
    public static string DeviceLabel(HttpContext context)
    {
        var agent = UserAgent(context) ?? string.Empty;

        var browser = Contains(agent, "Edg") ? "Edge"
            : Contains(agent, "OPR") || Contains(agent, "Opera") ? "Opera"
            : Contains(agent, "Chrome") ? "Chrome"
            : Contains(agent, "Firefox") ? "Firefox"
            : Contains(agent, "Safari") ? "Safari"
            : "Browser";

        var platform = Contains(agent, "Android") ? "Android"
            : Contains(agent, "iPhone") ? "iPhone"
            : Contains(agent, "iPad") ? "iPad"
            : Contains(agent, "Windows") ? "Windows"
            : Contains(agent, "Mac OS") || Contains(agent, "Macintosh") ? "Mac"
            : Contains(agent, "Linux") ? "Linux"
            : null;

        return platform is null ? browser : $"{browser} on {platform}";
    }

    private static bool Contains(string agent, string token)
        => agent.Contains(token, StringComparison.OrdinalIgnoreCase);
}
