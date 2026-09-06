using Microsoft.AspNetCore.Builder;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// The declaration verb for an endpoint that will not act on a session the caller has merely left open.
/// </summary>
/// <remarks>
/// <para>
/// Step-up is already enforced from the permission: a permission marked <c>RequiresStepUp</c> is refused
/// by <see cref="PermissionAuthorisationHandler"/> unless the caller re-authenticated recently. This verb
/// exists because that enforcement is invisible at the endpoint — a reader of the route, of the generated
/// API document or of the permission matrix cannot see the demand, and neither can a reviewer asking why
/// a screen prompts for a password again.
/// </para>
/// <para>
/// So it is both: a declaration that the matrix and ARCH-018 hold equal to the catalogue's flag, and a
/// second, independent gate. Two gates rather than one is deliberate. Clearing the flag on a permission
/// is a one-word edit that changes what every endpoint using it demands; the endpoint that named the
/// demand out loud keeps demanding it, and the architecture rule fails until somebody says in the same
/// pull request that they meant to weaken it.
/// </para>
/// </remarks>
public static class StepUpEndpointExtensions
{
    /// <summary>
    /// Declares that this endpoint acts only on a session that has re-authenticated within the
    /// configured freshness window.
    /// </summary>
    /// <param name="builder">The endpoint builder.</param>
    public static TBuilder RequireStepUp<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RequireAuthorization(policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new StepUpRequirement());
        });

        builder.WithMetadata(new StepUpMetadata());
        return builder;
    }
}

/// <summary>
/// Records that an endpoint demands a fresh re-authentication, for the endpoint inventory, the
/// authorisation matrix and ARCH-018.
/// </summary>
public sealed record StepUpMetadata;
