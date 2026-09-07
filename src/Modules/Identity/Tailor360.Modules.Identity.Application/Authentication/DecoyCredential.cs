using System.Security.Cryptography;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Identity.Application.Authentication;

/// <summary>
/// The shipped decoy: one synthetic account and one Argon2id hash, derived once for the life of the
/// process and verified against whenever a sign-in names an account that cannot be used.
/// </summary>
/// <remarks>
/// <para>
/// The derivation is deferred rather than done in the constructor. It costs a full Argon2id pass, and
/// doing it while the service provider is being built would add that to start-up for a deployment that
/// might never see an unknown sign-in name; <see cref="Lazy{T}"/> with the default thread-safety mode
/// means it happens once, on the first request that needs it, however many arrive at once.
/// </para>
/// <para>
/// The account is never added to a context and never saved. It exists to give the hasher something with
/// the shape it expects, because the hash is bound to the account it was made for.
/// </para>
/// </remarks>
/// <param name="hashing">The password hasher, used with the deployment's configured parameters.</param>
/// <param name="ids">Identifier generation.</param>
/// <param name="clock">The clock.</param>
public sealed class DecoyCredential(
    IPasswordHashingService hashing,
    IIdGenerator ids,
    IClock clock)
    : IDecoyCredential
{
    private readonly Lazy<Derived> _derived = new(() => Derive(hashing, ids, clock));

    /// <inheritdoc />
    public StaffUser User => _derived.Value.User;

    /// <inheritdoc />
    public string EncodedHash => _derived.Value.EncodedHash;

    private static Derived Derive(IPasswordHashingService hashing, IIdGenerator ids, IClock clock)
    {
        var invited = StaffUser.Invite(
            ids.NewId(),
            ids.NewId(),
            "unknown",
            "unknown@invalid.example",
            "Unknown",
            clock.UtcNow);

        var user = invited.Value;
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        return new Derived(user, hashing.Hash(user, secret));
    }

    private sealed record Derived(StaffUser User, string EncodedHash);
}
