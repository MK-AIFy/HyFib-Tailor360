using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Requires that the caller re-authenticated recently, independently of the permission they hold.
/// </summary>
/// <remarks>
/// It carries no data. What counts as recent is configuration — <see cref="StepUpOptions.Freshness"/> —
/// and a requirement that copied the window would let one endpoint drift from the rest.
/// </remarks>
public sealed class StepUpRequirement : IAuthorizationRequirement;

/// <summary>
/// Evaluates <see cref="StepUpRequirement"/> against the last time the caller proved a strong factor.
/// </summary>
/// <remarks>
/// The refusal is <see cref="AuthorisationRefusal.StepUpRequired"/> rather than a plain forbidden,
/// because the two mean different things to the client: this one is recoverable in place, by asking for
/// the factor again and retrying the pending request with the same idempotency key.
/// </remarks>
/// <param name="currentUser">The caller.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">Step-up freshness configuration.</param>
public sealed class StepUpAuthorisationHandler(
    ICurrentUser currentUser,
    IClock clock,
    IOptions<StepUpOptions> options)
    : AuthorizationHandler<StepUpRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StepUpRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        // A caller with no session has nothing to be stale; the authentication challenge answers first
        // and this stays a refusal rather than becoming an accidental pass.
        if (!StepUpFreshness.IsFresh(currentUser, clock, options.Value))
        {
            context.Fail(new RefusalReason(
                this, AuthorisationRefusal.StepUpRequired, "Recent re-authentication required."));

            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
