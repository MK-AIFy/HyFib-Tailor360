using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Credentials;

/// <summary>
/// The stored form of one account's password. The class holds an <em>encoded hash</em> and has no
/// member that could hold the password itself, so there is no code path through which a plaintext
/// password reaches the database.
/// </summary>
/// <remarks>
/// The hash is self-describing — it carries the algorithm and its parameters — so raising the work
/// factor later does not need a migration: the verifier reads the parameters out of each stored value
/// and reports that a rehash is due. That is why <see cref="Algorithm"/> exists as well: it is the
/// column an operator can group by to see how much of the estate is still on an old setting.
/// <para>
/// This is a class rather than a record deliberately. A record's generated <c>ToString</c> prints
/// every property, and a credential that lands in a log line because someone interpolated the object
/// is exactly the accident the type should make impossible.
/// </para>
/// </remarks>
public sealed class PasswordCredential
{
    /// <summary>The shortest value that can plausibly be an encoded hash rather than a password.</summary>
    public const int MinimumEncodedLength = 20;

    /// <summary>The longest encoded hash the store accepts.</summary>
    public const int MaximumEncodedLength = 512;

    /// <summary>The longest algorithm token the store accepts.</summary>
    public const int MaximumAlgorithmLength = 32;

    private PasswordCredential()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private PasswordCredential(Guid id, Guid userId, string encodedHash, string algorithm, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        EncodedHash = encodedHash;
        Algorithm = algorithm;
        CreatedAt = now;
    }

    /// <summary>Identity of this credential.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account this credential belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// The encoded hash, including its salt and parameters. Never a password, never reversible.
    /// </summary>
    public string EncodedHash { get; private set; } = string.Empty;

    /// <summary>The algorithm token the hash was produced with, for example <c>argon2id</c>.</summary>
    public string Algorithm { get; private set; } = string.Empty;

    /// <summary>When this credential was set.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the credential last verified a sign-in.</summary>
    public DateTimeOffset? LastVerifiedAt { get; private set; }

    /// <summary>
    /// True when the hash was produced with parameters weaker than the current configuration and should
    /// be replaced the next time the password is known — that is, during a successful sign-in.
    /// </summary>
    public bool RehashRequired { get; private set; }

    /// <summary>Creates a credential from an already-encoded hash.</summary>
    /// <param name="id">Identity of the credential, from <c>IIdGenerator</c>.</param>
    /// <param name="userId">The owning account.</param>
    /// <param name="encodedHash">The encoded hash produced by the password hasher.</param>
    /// <param name="algorithm">The algorithm token, for example <c>argon2id</c>.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    public static Result<PasswordCredential> Create(
        Guid id,
        Guid userId,
        string? encodedHash,
        string? algorithm,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<PasswordCredential>(IdentityErrors.Required("id"));
        }

        if (userId == Guid.Empty)
        {
            return Result.Failure<PasswordCredential>(IdentityErrors.Required("userId"));
        }

        if (!LooksEncoded(encodedHash))
        {
            return Result.Failure<PasswordCredential>(IdentityErrors.CredentialNotEncoded);
        }

        if (string.IsNullOrWhiteSpace(algorithm) || algorithm.Length > MaximumAlgorithmLength)
        {
            return Result.Failure<PasswordCredential>(IdentityErrors.Required("algorithm"));
        }

        return new PasswordCredential(id, userId, encodedHash!, algorithm, now);
    }

    /// <summary>Records that this credential verified a sign-in.</summary>
    public void RecordVerification(DateTimeOffset now) => LastVerifiedAt = now;

    /// <summary>Flags the stored hash as produced with parameters weaker than the current setting.</summary>
    public void MarkRehashRequired() => RehashRequired = true;

    /// <summary>Replaces the stored hash in place, which is what a transparent rehash does.</summary>
    public Result Replace(string? encodedHash, string? algorithm, DateTimeOffset now)
    {
        if (!LooksEncoded(encodedHash))
        {
            return Result.Failure(IdentityErrors.CredentialNotEncoded);
        }

        if (string.IsNullOrWhiteSpace(algorithm) || algorithm.Length > MaximumAlgorithmLength)
        {
            return Result.Failure(IdentityErrors.Required("algorithm"));
        }

        EncodedHash = encodedHash!;
        Algorithm = algorithm;
        CreatedAt = now;
        RehashRequired = false;

        return Result.Success();
    }

    /// <summary>
    /// A cheap structural check that the value is an encoded hash and not a password that slipped
    /// through. Passwords contain spaces and are short; encoded hashes are neither.
    /// </summary>
    private static bool LooksEncoded(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length is >= MinimumEncodedLength and <= MaximumEncodedLength
           && !value.Any(char.IsWhiteSpace);
}
