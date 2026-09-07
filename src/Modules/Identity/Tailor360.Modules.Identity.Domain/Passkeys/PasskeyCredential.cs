using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Passkeys;

/// <summary>
/// One WebAuthn credential registered against an account: the phishing-resistant factor, because the
/// browser binds the signature to the origin and will not sign for a look-alike domain.
/// </summary>
/// <remarks>
/// The stored material is a <em>public</em> key, so unlike every other credential in this module it is
/// not a secret. What must be protected instead is the signature counter. Authenticators that keep one
/// increment it on every assertion; if a value arrives that is not higher than the value stored, either
/// the credential has been copied out of its authenticator or an old assertion is being replayed.
/// Authenticators that keep no counter report zero forever, which the check has to allow — so "did not
/// advance" is a failure only when the stored counter is already above zero.
/// </remarks>
public sealed class PasskeyCredential
{
    /// <summary>The longest label the store accepts.</summary>
    public const int MaximumLabelLength = 100;

    /// <summary>The longest transport list the store accepts.</summary>
    public const int MaximumTransportsLength = 100;

    private PasskeyCredential()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private PasskeyCredential(
        Guid id,
        Guid userId,
        byte[] credentialId,
        byte[] publicKey,
        Guid authenticatorGuid,
        string label,
        long signatureCounter,
        DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        CredentialId = credentialId;
        PublicKey = publicKey;
        AuthenticatorGuid = authenticatorGuid;
        Label = label;
        SignatureCounter = signatureCounter;
        CreatedAt = now;
    }

    /// <summary>Identity of this registration.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account this credential belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The authenticator's credential identifier, unique within the account.</summary>
    public byte[] CredentialId { get; private set; } = [];

    /// <summary>The COSE-encoded public key. Not a secret.</summary>
    public byte[] PublicKey { get; private set; } = [];

    /// <summary>The authenticator model identifier reported at registration.</summary>
    public Guid AuthenticatorGuid { get; private set; }

    /// <summary>The name the holder gave this passkey, so they can tell two of them apart.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>The transports the authenticator advertised, as a comma-separated list.</summary>
    public string? Transports { get; private set; }

    /// <summary>The last signature counter value accepted from this authenticator.</summary>
    public long SignatureCounter { get; private set; }

    /// <summary>True when the credential may be copied to the holder's other devices.</summary>
    public bool IsBackupEligible { get; private set; }

    /// <summary>True when the credential currently is copied to the holder's other devices.</summary>
    public bool IsBackedUp { get; private set; }

    /// <summary>When the credential was registered.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the credential was last used to sign in.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>Registers a credential produced by a completed WebAuthn registration ceremony.</summary>
    public static Result<PasskeyCredential> Register(
        Guid id,
        Guid userId,
        byte[]? credentialId,
        byte[]? publicKey,
        Guid authenticatorGuid,
        string? label,
        long signatureCounter,
        DateTimeOffset now,
        string? transports = null,
        bool isBackupEligible = false,
        bool isBackedUp = false)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<PasskeyCredential>(IdentityErrors.Required("id"));
        }

        if (userId == Guid.Empty)
        {
            return Result.Failure<PasskeyCredential>(IdentityErrors.Required("userId"));
        }

        if (credentialId is null || credentialId.Length == 0)
        {
            return Result.Failure<PasskeyCredential>(IdentityErrors.Required("credentialId"));
        }

        if (publicKey is null || publicKey.Length == 0)
        {
            return Result.Failure<PasskeyCredential>(IdentityErrors.Required("publicKey"));
        }

        if (signatureCounter < 0)
        {
            return Result.Failure<PasskeyCredential>(IdentityErrors.Required("signatureCounter"));
        }

        var trimmedLabel = label?.Trim();
        if (string.IsNullOrEmpty(trimmedLabel))
        {
            return Result.Failure<PasskeyCredential>(IdentityErrors.Required("label"));
        }

        if (trimmedLabel.Length > MaximumLabelLength)
        {
            return Result.Failure<PasskeyCredential>(
                IdentityErrors.TooLong("label", MaximumLabelLength));
        }

        if (transports is { Length: > MaximumTransportsLength })
        {
            return Result.Failure<PasskeyCredential>(
                IdentityErrors.TooLong("transports", MaximumTransportsLength));
        }

        return new PasskeyCredential(
            id, userId, [.. credentialId], [.. publicKey], authenticatorGuid, trimmedLabel,
            signatureCounter, now)
        {
            Transports = transports,
            IsBackupEligible = isBackupEligible,
            IsBackedUp = isBackedUp,
        };
    }

    /// <summary>True when the supplied credential identifier is this credential's.</summary>
    public bool HasCredentialId(byte[]? candidate)
        => candidate is not null && CredentialId.AsSpan().SequenceEqual(candidate);

    /// <summary>
    /// Records a successful assertion, refusing a counter that did not advance when the authenticator
    /// keeps one.
    /// </summary>
    public Result RecordUse(long signatureCounter, DateTimeOffset now, bool isBackedUp)
    {
        if (signatureCounter < 0)
        {
            return Result.Failure(IdentityErrors.Required("signatureCounter"));
        }

        var authenticatorKeepsCounter = SignatureCounter > 0 || signatureCounter > 0;
        if (authenticatorKeepsCounter && signatureCounter <= SignatureCounter)
        {
            return Result.Failure(IdentityErrors.PasskeyCounterWentBackwards);
        }

        SignatureCounter = signatureCounter;
        IsBackedUp = isBackedUp;
        LastUsedAt = now;

        return Result.Success();
    }

    /// <summary>Renames the passkey.</summary>
    public Result Rename(string? label)
    {
        var trimmed = label?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure(IdentityErrors.Required("label"));
        }

        if (trimmed.Length > MaximumLabelLength)
        {
            return Result.Failure(IdentityErrors.TooLong("label", MaximumLabelLength));
        }

        Label = trimmed;
        return Result.Success();
    }
}
