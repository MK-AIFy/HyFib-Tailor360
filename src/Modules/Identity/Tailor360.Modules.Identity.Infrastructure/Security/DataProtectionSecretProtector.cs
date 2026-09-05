using Microsoft.AspNetCore.DataProtection;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Wraps the authenticator's shared secret with ASP.NET Core data protection.
/// </summary>
/// <remarks>
/// The purpose string is versioned. Data protection derives the key from it, so changing the string
/// changes the key: a value wrapped under <c>.v1</c> cannot be unwrapped under <c>.v2</c>. That is the
/// mechanism for deliberately retiring every stored secret at once, and it is the reason the string is
/// a constant here rather than a literal at a call site.
/// <para>
/// <b>This depends on the key ring being persisted.</b> A ring that is generated afresh on each start —
/// which is what an unconfigured application does in a container — makes every stored secret
/// unreadable at the next deployment and every enrolled staff member unable to sign in. Section 4.4
/// requires the ring in <c>platform.data_protection_keys</c>, checked by <c>/health/startup</c>.
/// </para>
/// </remarks>
/// <param name="provider">The data-protection provider.</param>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    /// <summary>The purpose the protector is derived for.</summary>
    public const string Purpose = "Tailor360.Identity.MfaSecret.v1";

    private readonly IDataProtector _protector =
        (provider ?? throw new ArgumentNullException(nameof(provider))).CreateProtector(Purpose);

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return _protector.Protect(plaintext);
    }

    /// <inheritdoc />
    public Result<string> Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return Result.Failure<string>(IdentityErrors.MfaSecretUnreadable);
        }

        try
        {
            return Result.Success(_protector.Unprotect(protectedValue));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // The realistic causes are operational — a key ring that was never persisted, or one that
            // has been replaced — so this is reported as a dependency failure the holder cannot fix,
            // not as a wrong answer. The exception itself never reaches a log or a response: it
            // carries key-ring detail that is of no use to a caller and of some use to an attacker.
            return Result.Failure<string>(IdentityErrors.MfaSecretUnreadable);
        }
    }
}
