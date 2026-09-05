using Microsoft.AspNetCore.Identity;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Adapts ASP.NET Core Identity's <see cref="IPasswordHasher{TUser}"/> to the application's port, so
/// that no handler names a hashing library and the algorithm token stored beside a hash comes from
/// whatever produced it.
/// </summary>
/// <param name="hasher">The registered hasher, which is Argon2id.</param>
public sealed class PasswordHashingService(IPasswordHasher<StaffUser> hasher) : IPasswordHashingService
{
    /// <inheritdoc />
    public string AlgorithmName => Argon2idPasswordHasher.AlgorithmName;

    /// <inheritdoc />
    public string Hash(StaffUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrEmpty(password);

        return hasher.HashPassword(user, password);
    }

    /// <inheritdoc />
    public PasswordVerification Verify(StaffUser user, string encodedHash, string password)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(encodedHash) || string.IsNullOrEmpty(password))
        {
            return PasswordVerification.Failed;
        }

        return hasher.VerifyHashedPassword(user, encodedHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerification.Succeeded,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SucceededButNeedsRehash,
            _ => PasswordVerification.Failed,
        };
    }
}
