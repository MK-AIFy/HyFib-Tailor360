using Microsoft.AspNetCore.Builder;
using Tailor360.Platform.Security.Antiforgery;

namespace Tailor360.Platform.Security.Endpoints;

/// <summary>Declares the few endpoints that a browser never reaches with the session cookie.</summary>
public static class AntiforgeryEndpointExtensions
{
    /// <summary>
    /// Exempts a state-changing endpoint from anti-forgery validation, recording why and where the
    /// exemption was reviewed. Everything else that changes state must present the token, including
    /// sign-in, the multi-factor challenge, passkey ceremonies and recovery: a forged sign-in from
    /// another origin is an attack in its own right, because it puts the attacker's account into the
    /// victim's browser and everything the victim then does is recorded against it.
    /// </summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="justification">Why no browser reaches this endpoint with the session cookie.</param>
    /// <param name="reviewedIn">The issue or threat model in which the exemption was reviewed.</param>
    public static TBuilder WithoutAntiforgeryValidation<TBuilder>(
        this TBuilder builder,
        string justification,
        string reviewedIn)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedIn);

        builder.WithMetadata(new AntiforgeryExemptionMetadata(justification, reviewedIn));
        return builder;
    }
}
