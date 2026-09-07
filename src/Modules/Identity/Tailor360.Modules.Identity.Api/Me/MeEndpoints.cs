using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Me;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Modules.Identity.Api.Me;

/// <summary>
/// What the client shell needs before it paints anything: who the caller is, how they want the
/// interface to behave, what their account still owes, and when this session ends.
/// </summary>
/// <remarks>
/// The preferences come from the account rather than from browser storage, which is what lets a tailor
/// move between the counter tablet and the workroom desktop without setting their language and text
/// size twice, and gives an administrator somewhere to look when asked why somebody sees the interface
/// in Tamil.
/// </remarks>
public static class MeEndpoints
{
    private const string Review = "#23, docs/security/threat-models/authentication.md";

    /// <summary>Maps the caller's own profile.</summary>
    /// <param name="me">The <c>/api/v1/me</c> group.</param>
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder me)
    {
        ArgumentNullException.ThrowIfNull(me);

        me.MapGet("/", async (
                HttpContext context,
                CurrentUserQuery query,
                ICurrentUser caller,
                SessionContext session,
                IOptions<SessionAuthenticationOptions> sessionOptions,
                CancellationToken cancellationToken) =>
            {
                var profile = await query.GetAsync(caller.UserId, cancellationToken);
                if (profile is null || session.Ticket is not { } ticket)
                {
                    // The session resolved a moment ago and the account has gone: it was deactivated
                    // between the authentication handler and here. Answering 401 rather than 404 is
                    // right — there is no caller any more, and the client's job is to sign in again.
                    return Problems.From(
                        IdentityApiErrors.SessionRequired, context, StatusCodes.Status401Unauthorized);
                }

                // The body carries the holder's own address and their account's security state. It is
                // for this person, on this request, and must not be cached by a shared browser.
                context.Response.Headers.CacheControl = "no-store";

                return Results.Ok(new CurrentUserResponse(
                    profile.UserId,
                    profile.UserName,
                    profile.DisplayName,
                    profile.Email,
                    profile.Status.ToString(),
                    ticket.OrganisationId,
                    ticket.ActiveBranchId,
                    [.. caller.Permissions.Order(StringComparer.Ordinal)],
                    new AccountSecurityPayload(
                        profile.MfaEnrolment.ToString(),
                        profile.MustChangePassword,
                        ticket.MfaSatisfied,
                        ticket.LastStrongAuthenticationAt,
                        new FactorAvailabilityPayload(
                            profile.HasAuthenticator,
                            profile.UnusedRecoveryCodes > 0,
                            profile.PasskeyCount > 0),
                        profile.UnusedRecoveryCodes),
                    PreferencesPayload.From(profile.Preferences),
                    SessionResponses.Expiry(ticket, sessionOptions.Value)));
            })
            .AllowPendingSignIn(
                "This endpoint returns the caller's own account, taken from the session and never from "
                + "the request, and is what the client shell reads before it can render anything at "
                + "all. A permission gating it would have to be held by every role to be useful, and "
                + "an account that lost it would be signed in but unable to load the interface. It "
                + "accepts a session that has not finished signing in because that is the state the "
                + "challenge and enrolment screens are painted in, and it discloses nothing to a "
                + "caller holding a stolen password that the sign-in response has not already told "
                + "them: whose account it is, and what it still owes.",
                Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithName("GetCurrentUser")
            .WithSummary("Return the caller's account, preferences and session expiry.")
            .Produces<CurrentUserResponse>(StatusCodes.Status200OK)
            .WithTags(IdentityRoutes.AuthTag);

        return me;
    }
}
