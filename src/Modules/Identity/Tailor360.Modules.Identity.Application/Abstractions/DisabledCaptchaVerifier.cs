using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Options;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// The verifier registered when no provider has been chosen: it reports itself off, and the sign-in
/// path therefore never asks anyone to prove they are human.
/// </summary>
/// <remarks>
/// It answers <see langword="false"/> to <see cref="VerifyAsync"/> rather than <see langword="true"/>.
/// That looks odd for a no-op and is the important part: if a deployment turns the feature on without
/// registering a provider, every attempt that reaches the check is refused rather than waved through.
/// The two states a misconfiguration can produce are "nobody is asked" and "nobody passes", and both
/// are safe.
/// </remarks>
/// <param name="options">The captcha configuration.</param>
public sealed class DisabledCaptchaVerifier(IOptions<CaptchaOptions> options) : ICaptchaVerifier
{
    /// <inheritdoc />
    public bool IsEnabled => options.Value.Enabled;

    /// <inheritdoc />
    public Task<bool> VerifyAsync(
        string? response,
        string? clientKey,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
