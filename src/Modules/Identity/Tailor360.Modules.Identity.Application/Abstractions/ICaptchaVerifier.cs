namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Checks a human-verification response, when a deployment has chosen a provider to check it with.
/// </summary>
/// <remarks>
/// The seam exists so that the sign-in path is written once, with the check in it, whether or not a
/// provider is configured. Adding one later is a registration; it is not a change to the code that
/// decides whether someone may sign in, which is the code least worth editing twice.
/// <para>
/// An implementation must fail closed: a provider that cannot be reached means the answer could not be
/// verified, which is not the same as it being right.
/// </para>
/// </remarks>
public interface ICaptchaVerifier
{
    /// <summary>True when a provider is configured and should be consulted.</summary>
    bool IsEnabled { get; }

    /// <summary>Checks one response.</summary>
    /// <param name="response">The token the client's widget produced. Never logged.</param>
    /// <param name="clientKey">The client address, when the provider binds the answer to one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> VerifyAsync(string? response, string? clientKey, CancellationToken cancellationToken = default);
}
