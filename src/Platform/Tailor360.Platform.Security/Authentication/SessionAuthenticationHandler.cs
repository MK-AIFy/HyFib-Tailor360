using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tailor360.Platform.Security.Authentication;

/// <summary>
/// The one authentication scheme a browser uses. It takes the opaque value in the
/// <c>__Host-t360.session</c> cookie, asks the ticket store what it names, and builds the principal
/// from what comes back — never from anything the cookie itself carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the cookie holds no claims.</b> A signed token containing the holder's identity and
/// permissions has to be honoured until it expires, because the server has nothing to check it
/// against. That makes "sign out everywhere", "suspend this account" and "this person no longer works
/// here" promises the system cannot keep for the life of the token. Resolving a 256-bit opaque value
/// against a row on every request costs one indexed lookup and makes all three immediate: the
/// revocation is a column on that row, and this handler reads it before anything else runs.
/// </para>
/// <para>
/// <b>Why a rejected cookie is cleared.</b> A browser holding a revoked or expired value would keep
/// sending it on every request for the rest of the browsing session, and the person would keep being
/// told they are not signed in while their browser insists otherwise. Clearing it here makes the next
/// request an honest "no ticket".
/// </para>
/// <para>
/// The presented value is a credential. It is never logged, never put in a problem-details response and
/// never included in a trace attribute; the session identifier is what appears in diagnostics, and it
/// is useless to anyone who does not already hold the cookie.
/// </para>
/// </remarks>
/// <param name="options">Scheme options.</param>
/// <param name="logger">Logger factory.</param>
/// <param name="encoder">URL encoder.</param>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// Names why a request that presented a cookie is not authenticated, so the client can tell a
    /// session that ended apart from one that never existed and show the right screen. It says nothing
    /// a holder of the cookie does not already know.
    /// </summary>
    public const string SessionStateHeader = "X-Session-State";

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sessionContext = Context.RequestServices.GetRequiredService<SessionContext>();

        var presented = SessionCookie.Read(Request);
        if (presented is null)
        {
            sessionContext.Set(SessionResolution.NoTicket);
            return AuthenticateResult.NoResult();
        }

        var store = Context.RequestServices.GetRequiredService<ISessionTicketStore>();
        var resolution = await store.ResolveAsync(presented, Context.RequestAborted);
        sessionContext.Set(resolution);

        if (resolution.Ticket is not { } ticket || !resolution.IsActive)
        {
            RejectPresentedCookie(resolution.Status);
            return AuthenticateResult.Fail("The session cookie does not name a usable session.");
        }

        var principal = BuildPrincipal(ticket);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private void RejectPresentedCookie(SessionTicketStatus status)
    {
        // A response that has already begun cannot be corrected; the next request will present the
        // same cookie and be refused again, which is the safe direction to fail in.
        if (Response.HasStarted)
        {
            return;
        }

        SessionCookie.Clear(Response);
        Response.Headers[SessionStateHeader] = status switch
        {
            SessionTicketStatus.Revoked => "revoked",
            SessionTicketStatus.Expired => "expired",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Builds the framework principal. It carries the identity only: the permission and branch model
    /// is read from the ticket through <c>ICurrentUser</c>, not from claims, so there is exactly one
    /// answer to "what may this caller do" and it is the one the database gave a moment ago.
    /// </summary>
    private ClaimsPrincipal BuildPrincipal(SessionTicket ticket)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, ticket.UserId.ToString("n")),

                // The anti-forgery token is bound to this claim, so it has to be the stable account
                // identifier rather than the session or the display name: a token stays valid across a
                // session rotation, and does not survive a change of account.
                new Claim(ClaimTypes.Name, ticket.UserId.ToString("n")),
            ],
            Scheme.Name,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }
}
