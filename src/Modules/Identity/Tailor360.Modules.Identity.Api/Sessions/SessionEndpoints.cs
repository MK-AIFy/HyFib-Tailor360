using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Authentication;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Modules.Identity.Api.Sessions;

/// <summary>
/// The holder's own session and device inventory: where their account is signed in, and how to end one
/// of those sessions.
/// </summary>
/// <remarks>
/// This is the screen a person reaches for when they think somebody else is in their account, so both
/// endpoints are gated on holding a session rather than on a permission an administrator might not have
/// granted. Revoking names a session identifier, which makes it the one endpoint here that could be
/// pointed at somebody else's record; the handler checks that the session is on the caller's own
/// account first, and answers "not yours" and "does not exist" identically.
/// </remarks>
public static class SessionEndpoints
{
    private const string Review = "#23, docs/security/threat-models/authentication.md";

    private const string Justification =
        "The session inventory lists the caller's own sessions, taken from the session making the "
        + "request, and the revoke endpoint refuses any session that is not on that account. No "
        + "permission could gate these correctly: seeing where your own account is signed in, and "
        + "ending one of those sessions, is what a person does when they suspect their password is "
        + "known, and it must not depend on a grant somebody forgot to make.";

    /// <summary>Maps the session inventory.</summary>
    /// <param name="sessions">The <c>/api/v1/sessions</c> group.</param>
    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        sessions.MapGet("/", async (
                HttpContext context,
                ISessionService service,
                ICurrentUser caller,
                SessionContext session,
                CancellationToken cancellationToken) =>
            {
                var inventory = await service.ListForUserAsync(
                    caller.UserId, session.SessionId, cancellationToken);

                // The inventory names the addresses the account is signed in from, which is personal
                // data shown to its subject and to nobody else. It must not be cached anywhere between
                // here and the screen, least of all on a shared counter device.
                context.Response.Headers.CacheControl = "no-store";

                return Results.Ok(inventory.Select(SessionPayload.From).ToArray());
            })
            .RequireSignedInHolder(Justification, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithName("ListSessions")
            .WithSummary("List the devices the caller's account is signed in on.")
            .WithTags(IdentityRoutes.SessionTag);

        sessions.MapDelete("/{sessionId:guid}", async (
                Guid sessionId,
                HttpContext context,
                SignOutHandler handler,
                ICurrentUser caller,
                SessionContext session,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.RevokeAsync(
                    caller.UserId, sessionId, session.SessionId, cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context);
                }

                // Revoking the session you are using is a legitimate thing to do from this screen — it
                // is "sign this device out" — so the cookie goes with it.
                if (session.SessionId == sessionId)
                {
                    SessionCookie.Clear(context.Response);
                }

                return Results.NoContent();
            })
            .RequireSignedInHolder(Justification, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(SignOutHandler.SessionRevokedAction)
            .WithName("RevokeSession")
            .WithSummary("End one session on the caller's account.")
            .WithTags(IdentityRoutes.SessionTag);

        return sessions;
    }
}
