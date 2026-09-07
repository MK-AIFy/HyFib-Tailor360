using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Abuse;
using Tailor360.Modules.Identity.Application.Passkeys;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Modules.Identity.Api.Authentication;

/// <summary>
/// Registering, listing, removing and signing in with passkeys.
/// </summary>
/// <remarks>
/// A passkey is the only credential here that is both factors at once and the only one that is
/// phishing-resistant, because the browser binds its signature to the origin and will not sign for a
/// look-alike domain however convincing the page is. A completed assertion therefore starts a session
/// with the second factor already satisfied, and no challenge follows.
/// </remarks>
public static class PasskeyEndpoints
{
    /// <summary>The audit action recorded when a ceremony is started.</summary>
    public const string CeremonyStartedAction = "identity.passkey.ceremony-started";

    private const string Review = "#23, docs/security/threat-models/authentication.md";

    private const string ListJustification =
        "Listing the caller's passkeys acts on their own account, taken from the session and never from "
        + "the request. No permission could gate it correctly: seeing which authenticators are "
        + "registered against your own account is how you notice one you do not recognise.";

    private const string RegistrationJustification =
        "Registering a passkey acts on the caller's own account, taken from the session and never from "
        + "the request, and no permission could gate it correctly — an account that could be refused "
        + "permission to protect itself would be an account nobody could secure. A passkey is both "
        + "factors at once, so registering one against an account that already holds a factor is the "
        + "whole attack in one request; the handler therefore refuses unless the session making the "
        + "request has satisfied that factor. An account with no confirmed factor is the narrow "
        + "exception, because somebody has to be able to register the first one.";

    private const string RemovalJustification =
        "Removing a passkey acts on the caller's own account, taken from the session and never from the "
        + "request. It demands a satisfied second factor, because removing one is a change to the "
        + "account's protection and a caller holding only a stolen password must not be able to make it; "
        + "and the domain refuses to remove the last remaining factor at all, so this endpoint cannot "
        + "leave an account without one. Reaching zero is an administrator's reset, which takes a reason "
        + "and is audited.";

    private const string AnonymousJustification =
        "A passkey sign-in cannot require a session, because it is what creates one — and unlike the "
        + "password path there is no name to type, so nothing identifies the caller until the "
        + "authenticator has signed. The exposure is bounded the same way sign-in is: the anti-forgery "
        + "token is required, the origin check runs first, and a per-address rate-limit policy applies. "
        + "The challenge itself is 256 bits of server entropy held server-side, spent on first use and "
        + "bound to the ceremony handle, so an assertion issued to one browser cannot be completed by "
        + "another. Every failure — unknown credential, suspended account, bad signature — answers "
        + "identically, so nothing here reveals whether an account exists.";

    /// <summary>Maps the passkey endpoints.</summary>
    /// <param name="auth">The <c>/api/v1/auth</c> group.</param>
    public static RouteGroupBuilder MapPasskeyEndpoints(this RouteGroupBuilder auth)
    {
        ArgumentNullException.ThrowIfNull(auth);

        MapRegistration(auth);
        MapAssertion(auth);
        MapManagement(auth);

        return auth;
    }

    private static void MapRegistration(RouteGroupBuilder auth)
    {
        auth.MapPost("/passkeys/register/options", async (
                HttpContext context,
                PasskeyHandler handler,
                ICurrentUser caller,
                SessionContext session,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.BeginRegistrationAsync(
                    RequestFacts.Caller(caller, session), cancellationToken);

                return result.IsFailure ? Problems.From(result.Error, context) : Challenge(context, result.Value);
            })
            .RequireSignedInHolder(RegistrationJustification, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(CeremonyStartedAction)
            .WithName("BeginPasskeyRegistration")
            .WithSummary("Start registering a passkey and return the WebAuthn creation options.")
            .Produces<PasskeyChallengeResponse>(StatusCodes.Status200OK)
            .WithTags(IdentityRoutes.AuthTag);

        auth.MapPost("/passkeys/register", async (
                PasskeyRegistrationRequest request,
                HttpContext context,
                PasskeyHandler handler,
                ICurrentUser caller,
                SessionContext session,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.CompleteRegistrationAsync(
                    RequestFacts.Caller(caller, session),
                    request.CeremonyId,
                    request.Credential.GetRawText(),
                    request.Label,
                    cancellationToken);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(Describe(result.Value));
            })
            .RequireSignedInHolder(RegistrationJustification, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PasskeyHandler.RegisteredAction)
            .WithName("CompletePasskeyRegistration")
            .WithSummary("Finish registering a passkey.")
            .Produces<PasskeyPayload>(StatusCodes.Status200OK)
            .WithTags(IdentityRoutes.AuthTag);
    }

    private static void MapAssertion(RouteGroupBuilder auth)
    {
        auth.MapPost("/passkeys/assert/options", (
                HttpContext context,
                PasskeyHandler handler,
                SessionContext session) =>
            {
                var result = handler.BeginAssertion(session.SessionId);
                return result.IsFailure ? Problems.From(result.Error, context) : Challenge(context, result.Value);
            })
            .AllowAnonymousWithJustification(AnonymousJustification, Review)
            .RequireRateLimiting(RateLimitPolicyNames.AuthenticationAnonymous)
            .Audited(CeremonyStartedAction)
            .WithName("BeginPasskeyAssertion")
            .WithSummary("Start a passkey sign-in and return the WebAuthn request options.")
            .Produces<PasskeyChallengeResponse>(StatusCodes.Status200OK)
            .WithTags(IdentityRoutes.AuthTag);

        auth.MapPost("/passkeys/assert", async (
                PasskeyAssertionRequest request,
                HttpContext context,
                PasskeyHandler handler,
                ICredentialThrottle throttle,
                SessionContext session,
                IOptions<SessionAuthenticationOptions> sessionOptions,
                CancellationToken cancellationToken) =>
            {
                var clientKey = RequestFacts.ClientAddress(context);

                // No account is named, so only the address can be counted. It is still worth counting:
                // a caller replaying captured assertions is doing it from somewhere.
                var decision = throttle.Check(CredentialAction.SignIn, accountKey: null, clientKey);
                if (!decision.IsAllowed)
                {
                    return Problems.TooManyAttempts(
                        context,
                        decision.RetryAfter,
                        IdentityApiErrors.TooManyAttemptsCode,
                        "Too many sign-in attempts have been made. Wait and try again.");
                }

                var result = await handler.CompleteAssertionAsync(
                    new PasskeyAssertionCommand(
                        request.CeremonyId,
                        request.Credential.GetRawText(),
                        RequestFacts.DeviceLabel(context),
                        clientKey,
                        RequestFacts.UserAgent(context),
                        session.SessionId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context, StatusCodes.Status401Unauthorized);
                }

                SessionCookie.Issue(context.Response, result.Value.Session.Token);

                return Results.Ok(new SignInResponse(
                    "complete",
                    result.Value.UserId,
                    result.Value.DisplayName,
                    result.Value.MustChangePassword,
                    new FactorAvailabilityPayload(
                        result.Value.Factors.Authenticator,
                        result.Value.Factors.RecoveryCode,
                        result.Value.Factors.Passkey),
                    SessionResponses.Expiry(result.Value.Session, sessionOptions.Value)));
            })
            .AllowAnonymousWithJustification(AnonymousJustification, Review)
            .RequireRateLimiting(RateLimitPolicyNames.AuthenticationAnonymous)
            .Audited(PasskeyHandler.SignedInAction)
            .WithName("CompletePasskeyAssertion")
            .WithSummary("Sign in with a passkey, which satisfies both factors.")
            .Produces<SignInResponse>(StatusCodes.Status200OK)
            .WithTags(IdentityRoutes.AuthTag);
    }

    private static void MapManagement(RouteGroupBuilder auth)
    {
        auth.MapGet("/passkeys", async (
                PasskeyHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var passkeys = await handler.ListAsync(caller.UserId, cancellationToken);
                return Results.Ok(passkeys.Select(Describe).ToArray());
            })
            .RequireSignedInHolder(ListJustification, Review)
            .RequireRateLimiting(RateLimitPolicyNames.DefaultUser)
            .WithName("ListPasskeys")
            .WithSummary("List the passkeys registered against the caller's account.")
            .Produces<IReadOnlyList<PasskeyPayload>>(StatusCodes.Status200OK)
            .WithTags(IdentityRoutes.AuthTag);

        auth.MapDelete("/passkeys/{passkeyId:guid}", async (
                Guid passkeyId,
                HttpContext context,
                PasskeyHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.RemoveAsync(caller.UserId, passkeyId, cancellationToken);
                return result.IsFailure ? Problems.From(result.Error, context) : Results.NoContent();
            })
            .RequireSatisfiedSecondFactor(RemovalJustification, Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(PasskeyHandler.RemovedAction)
            .WithName("RemovePasskey")
            .WithSummary("Remove one of the caller's passkeys.")
            .Produces(StatusCodes.Status204NoContent)
            .WithTags(IdentityRoutes.AuthTag);
    }

    /// <summary>
    /// Returns the options object the browser needs. It is passed through verbatim as JSON rather than
    /// re-modelled: the shape is the W3C specification's, and restating it here would create a second
    /// definition to keep in step with a specification this system does not own.
    /// </summary>
    private static IResult Challenge(HttpContext context, PasskeyChallenge challenge)
    {
        // A challenge is single-use and lives for minutes. A cached copy is either useless or a
        // replayable one, so nothing between here and the browser may keep it.
        context.Response.Headers.CacheControl = "no-store";

        using var document = JsonDocument.Parse(challenge.OptionsJson);

        return Results.Ok(new PasskeyChallengeResponse(
            challenge.CeremonyId, document.RootElement.Clone(), challenge.ExpiresAt));
    }

    private static PasskeyPayload Describe(RegisteredPasskey passkey) => new(
        passkey.PasskeyId,
        passkey.Label,
        passkey.CreatedAt,
        passkey.LastUsedAt,
        passkey.IsBackedUp);
}
