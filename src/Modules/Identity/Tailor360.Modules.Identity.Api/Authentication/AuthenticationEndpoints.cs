using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Api.Payloads;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Abuse;
using Tailor360.Modules.Identity.Application.Authentication;
using Tailor360.Modules.Identity.Application.Mfa;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;

namespace Tailor360.Modules.Identity.Api.Authentication;

/// <summary>Signing in, answering a second factor, and signing out.</summary>
public static class AuthenticationEndpoints
{
    /// <summary>The justification recorded against every anonymous endpoint in this group.</summary>
    private const string Review = "#23, docs/security/threat-models/authentication.md";

    /// <summary>Maps the sign-in and sign-out endpoints.</summary>
    /// <param name="auth">The <c>/api/v1/auth</c> group.</param>
    public static RouteGroupBuilder MapAuthenticationEndpoints(this RouteGroupBuilder auth)
    {
        ArgumentNullException.ThrowIfNull(auth);

        MapSignIn(auth);
        MapChallenge(auth);
        MapSignOut(auth);

        return auth;
    }

    private static void MapSignIn(RouteGroupBuilder auth)
        => auth.MapPost("/login", async (
                SignInRequest request,
                HttpContext context,
                SignInHandler handler,
                ICredentialThrottle throttle,
                ICaptchaVerifier captcha,
                SessionContext session,
                IOptions<SessionAuthenticationOptions> sessionOptions,
                CancellationToken cancellationToken) =>
            {
                var clientKey = RequestFacts.ClientAddress(context);

                // The throttle runs before anything expensive. A password verification is tens of
                // milliseconds of CPU and a megabyte of memory by design, and an unauthenticated caller
                // must not be able to spend them at will. It is keyed on the normalised identifier, not
                // on the raw text, because the directory resolves the two forms to the same account and
                // two spellings must not buy two budgets; the handler checks again on the resolved
                // account once it knows which one it is.
                var decision = throttle.Check(
                    CredentialAction.SignIn, SignInIdentifier.Normalise(request.Identifier), clientKey);
                if (!decision.IsAllowed)
                {
                    return Problems.TooManyAttempts(
                        context,
                        decision.RetryAfter,
                        IdentityApiErrors.TooManyAttemptsCode,
                        "Too many sign-in attempts have been made. Wait and try again.");
                }

                if (decision.IsCaptchaRequired
                    && captcha.IsEnabled
                    && !await captcha.VerifyAsync(request.CaptchaResponse, clientKey, cancellationToken))
                {
                    return Problems.From(IdentityApiErrors.CaptchaRequired, context);
                }

                var result = await handler.SignInAsync(
                    new SignInCommand(
                        request.Identifier,
                        request.Password,
                        RequestFacts.DeviceLabel(context),
                        clientKey,
                        RequestFacts.UserAgent(context),
                        TrustedDeviceCookie.Read(context.Request),
                        session.SessionId),
                    cancellationToken);

                if (result.IsFailure)
                {
                    // 401 rather than 403: the instruction to the client is "authenticate", not "stop".
                    return Problems.From(result.Error, context, StatusCodes.Status401Unauthorized);
                }

                SessionCookie.Issue(context.Response, result.Value.Session.Token);

                return Results.Ok(SignInResponse.From(
                    result.Value,
                    SessionResponses.Expiry(result.Value.Session, sessionOptions.Value)));
            })
            .AllowAnonymousWithJustification(
                "Signing in is the one request that cannot require a session, because it is what "
                + "creates one. The exposure is bounded by controls that do not depend on a principal: "
                + "the anti-forgery token is required here as everywhere else, so a sign-in forged from "
                + "another origin — which would plant the attacker's account in the victim's browser and "
                + "record everything the victim then does against it — is refused; the origin check "
                + "rejects a cross-site attempt before it reaches this handler; a per-address rate-limit "
                + "policy and a per-account throttle bound guessing; and the progressive lockout on the "
                + "account survives both. Every refusal is worded identically, so this endpoint cannot "
                + "be used to discover who holds an account.",
                Review)
            .RequireRateLimiting(RateLimitPolicyNames.AuthenticationAnonymous)
            .Audited(SignInHandler.SucceededAction)
            .WithName("SignIn")
            .WithSummary("Answer the first factor and start a session.")
            .Produces<SignInResponse>(StatusCodes.Status200OK)
            .WithTags(IdentityRoutes.AuthTag);

    private static void MapChallenge(RouteGroupBuilder auth)
        => auth.MapPost("/mfa/challenge", async (
                MultiFactorChallengeRequest request,
                HttpContext context,
                MultiFactorSignInHandler handler,
                ICredentialThrottle throttle,
                ICurrentUser caller,
                SessionContext session,
                IOptions<SessionAuthenticationOptions> sessionOptions,
                CancellationToken cancellationToken) =>
            {
                if (session.SessionId is not { } sessionId)
                {
                    return Problems.From(
                        IdentityApiErrors.SessionRequired, context, StatusCodes.Status401Unauthorized);
                }

                if (ReadFactor(request.Factor) is not { } factor)
                {
                    return Problems.From(IdentityApiErrors.FactorNotRecognised, context);
                }

                var clientKey = RequestFacts.ClientAddress(context);
                var accountKey = caller.UserId.ToString("n");

                var decision = throttle.Check(CredentialAction.MultiFactorChallenge, accountKey, clientKey);
                if (!decision.IsAllowed)
                {
                    return Problems.TooManyAttempts(
                        context,
                        decision.RetryAfter,
                        IdentityApiErrors.TooManyAttemptsCode,
                        "Too many codes have been tried. Wait and try again.");
                }

                var result = await handler.AnswerAsync(
                    new MultiFactorAnswer(
                        sessionId,
                        caller.UserId,
                        factor,
                        request.Code,
                        request.RememberDevice,
                        RequestFacts.DeviceLabel(context),
                        clientKey),
                    cancellationToken);

                if (result.IsFailure)
                {
                    return Problems.From(result.Error, context, StatusCodes.Status401Unauthorized);
                }

                // The session is replaced, so the cookie must be too. Failing to write it here would
                // leave the browser holding the ticket that was just revoked, which reads to the person
                // as being signed out by answering correctly.
                SessionCookie.Issue(context.Response, result.Value.Session.Token);

                if (result.Value is { TrustedDeviceToken: { } deviceToken, TrustedDeviceExpiresAt: { } until })
                {
                    TrustedDeviceCookie.Issue(context.Response, deviceToken, until);
                }

                return Results.Ok(new MultiFactorChallengeResponse(
                    result.Value.RemainingRecoveryCodes,
                    result.Value.ShouldReissueRecoveryCodes,
                    result.Value.TrustedDeviceToken is not null,
                    SessionResponses.Expiry(result.Value.Session, sessionOptions.Value)));
            })
            .AllowPendingSignIn(
                "The challenge is answered by the session the first factor created, which is why it "
                + "needs a session and not a permission: the account is taken from that session and "
                + "never from the request, so this endpoint cannot be pointed at somebody else. It is "
                + "one of the four endpoints a caller who has not finished signing in may reach, "
                + "because it is what finishing consists of. That session has satisfied no second "
                + "factor and reaches nothing that requires one — including anything that could mint "
                + "the answer to this challenge — until this endpoint replaces it with one that has.",
                Review)
            .RequireRateLimiting(RateLimitPolicyNames.MultiFactorChallenge)
            .Audited(MultiFactorSignInHandler.SatisfiedAction)
            .WithName("AnswerMultiFactorChallenge")
            .WithSummary("Answer a second-factor challenge with an authenticator or recovery code.")
            .Produces<MultiFactorChallengeResponse>(StatusCodes.Status200OK)
            .WithTags(IdentityRoutes.AuthTag);

    private static void MapSignOut(RouteGroupBuilder auth)
    {
        auth.MapPost("/logout", async (
                HttpContext context,
                SignOutHandler handler,
                ICurrentUser caller,
                SessionContext session,
                CancellationToken cancellationToken) =>
            {
                if (session.SessionId is { } sessionId)
                {
                    await handler.SignOutAsync(caller.UserId, sessionId, cancellationToken);
                }

                // The cookie is cleared whatever the server found. A person who presses "sign out"
                // must end up signed out of the browser even if the session had already lapsed, and
                // leaving a dead cookie behind makes the next request look like a revoked session
                // rather than an anonymous one.
                SessionCookie.Clear(context.Response);

                return Results.NoContent();
            })
            .AllowPendingSignIn(
                "Signing out acts on the session making the request and on nothing else. No permission "
                + "could gate it: everybody who can sign in must be able to sign out — including "
                + "somebody who has answered only the first factor and has thought better of "
                + "continuing, which is why it accepts a session that has not finished signing in.",
                Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(SignOutHandler.SignedOutAction)
            .WithName("SignOut")
            .WithSummary("End the session this request is being made under.")
            .Produces(StatusCodes.Status204NoContent)
            .WithTags(IdentityRoutes.AuthTag);

        auth.MapPost("/logout-all", async (
                HttpContext context,
                SignOutHandler handler,
                ICurrentUser caller,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.SignOutEverywhereAsync(caller.UserId, cancellationToken);

                SessionCookie.Clear(context.Response);

                // The remembered-device cookie goes too. The server has already forgotten the device,
                // so leaving the value in the browser would only mean it was presented and refused on
                // every later sign-in.
                TrustedDeviceCookie.Clear(context.Response);

                return result.IsFailure
                    ? Problems.From(result.Error, context)
                    : Results.Ok(new SignOutEverywhereResponse(result.Value));
            })
            .RequireSignedInHolder(
                "Signing out everywhere ends every session on the caller's own account, taken from the "
                + "session making the request. It is the control a person reaches for when they think "
                + "someone else has their password, so it must not be gated on a permission that an "
                + "administrator could have failed to grant them. It does require a finished sign-in: "
                + "a half session can end itself through /logout, and ending somebody's other sessions "
                + "is a denial of service a caller who has proved only a password should not have.",
                Review)
            .RequireRateLimiting(RateLimitPolicyNames.Write)
            .Audited(SignOutHandler.SignedOutEverywhereAction)
            .WithName("SignOutEverywhere")
            .WithSummary("End every session on the caller's account.")
            .Produces<SignOutEverywhereResponse>(StatusCodes.Status200OK)
            .WithTags(IdentityRoutes.AuthTag);
    }

    private static MfaFactor? ReadFactor(string? factor) => factor?.Trim().ToLowerInvariant() switch
    {
        "totp" or "authenticator" => MfaFactor.Totp,
        "recoverycode" or "recovery-code" or "recovery_code" => MfaFactor.RecoveryCode,
        _ => null,
    };
}
