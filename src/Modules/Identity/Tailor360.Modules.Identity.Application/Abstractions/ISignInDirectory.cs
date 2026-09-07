using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// The reads the sign-in path needs, which are not the reads the enrolment and recovery handlers need.
/// </summary>
/// <remarks>
/// It is a separate port from <see cref="IIdentityStore"/> on purpose. Sign-in looks an account up by
/// something a person typed — which may be either a sign-in name or an email address — and a passkey
/// assertion looks one up by a credential identifier the browser supplied with no account named at all.
/// Neither of those belongs on a store described as "what the multi-factor and recovery handlers do",
/// and folding them in would make that store the general repository it was written not to be.
/// <para>
/// Both lookups return the whole aggregate, for the same reason the other store's do: every rule the
/// sign-in path applies spans the password, the second factor and the remembered devices at once.
/// </para>
/// </remarks>
public interface ISignInDirectory
{
    /// <summary>
    /// Finds the account a person named, by sign-in name or by email address.
    /// </summary>
    /// <param name="identifier">
    /// Exactly what was typed. The implementation normalises — names fold to lower case, addresses to
    /// upper — so that one place decides how a typed identifier becomes a lookup.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StaffUser?> FindForSignInAsync(string? identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the account holding a passkey, from the credential identifier the browser asserted with.
    /// This is what makes a sign-in possible with no name typed at all.
    /// </summary>
    Task<StaffUser?> FindByPasskeyAsync(byte[]? credentialId, CancellationToken cancellationToken = default);

    /// <summary>Loads one account by identity, with the whole aggregate.</summary>
    Task<StaffUser?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Commits the changes made through this port.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
