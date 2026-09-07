using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Sessions;

/// <summary>
/// A device the holder has told the system to remember, so that later sign-ins from it need only the
/// password rather than a fresh second-factor challenge. Shared counter tablets are the reason it
/// exists: a receptionist who signs in twenty times a shift will otherwise route around the control.
/// </summary>
/// <remarks>
/// Remembering a device skips the <em>challenge</em>. It never counts as a strong authentication, so a
/// session started this way still has no fresh <c>last_strong_auth_at</c> and every step-up endpoint
/// still asks. That distinction is what keeps a remembered device from becoming a permanent bypass of
/// the controls on administration and billing.
/// <para>
/// The token is stored as a digest like every other, the lifetime is capped at thirty days, and the
/// device appears in the holder's own inventory where it can be revoked — revocation being final, as
/// it is for a session.
/// </para>
/// </remarks>
public sealed class TrustedDevice
{
    /// <summary>The longest a device may be remembered.</summary>
    public const int MaximumLifetimeDays = 30;

    /// <summary>The longest label the store accepts.</summary>
    public const int MaximumLabelLength = 100;

    private TrustedDevice()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private TrustedDevice(
        Guid id,
        Guid userId,
        string tokenHash,
        string label,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        Label = label;
        CreatedAt = now;
        LastSeenAt = now;
        ExpiresAt = expiresAt;
    }

    /// <summary>Identity of this remembered device.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that remembered it.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The SHA-256 digest of the device cookie value. Never the value itself.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>What the holder sees in the inventory, such as "Counter tablet".</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>When the device was first remembered.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the device was last used to skip a challenge.</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>When the memory of the device lapses.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the holder or an administrator revoked it.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>True while the device may still skip a second-factor challenge.</summary>
    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>Remembers a device.</summary>
    /// <param name="id">Identity of the record, from <c>IIdGenerator</c>.</param>
    /// <param name="userId">The account remembering it.</param>
    /// <param name="tokenHash">SHA-256 digest of the device cookie value.</param>
    /// <param name="label">A human label for the inventory.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="lifetime">How long to remember it, capped at thirty days.</param>
    public static Result<TrustedDevice> Remember(
        Guid id,
        Guid userId,
        string? tokenHash,
        string? label,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<TrustedDevice>(IdentityErrors.Required("id"));
        }

        if (userId == Guid.Empty)
        {
            return Result.Failure<TrustedDevice>(IdentityErrors.Required("userId"));
        }

        if (!HashedSecret.IsWellFormed(tokenHash))
        {
            return Result.Failure<TrustedDevice>(IdentityErrors.TokenNotHashed("trustedDeviceToken"));
        }

        var trimmed = label?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<TrustedDevice>(IdentityErrors.Required("label"));
        }

        if (trimmed.Length > MaximumLabelLength)
        {
            trimmed = trimmed[..MaximumLabelLength];
        }

        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(MaximumLifetimeDays))
        {
            return Result.Failure<TrustedDevice>(
                IdentityErrors.TrustedDeviceLifetimeTooLong(MaximumLifetimeDays));
        }

        return new TrustedDevice(id, userId, tokenHash!, trimmed, now, now + lifetime);
    }

    /// <summary>True when the supplied digest is this device's, compared in fixed time.</summary>
    public bool Matches(string? candidateHash) => HashedSecret.Matches(TokenHash, candidateHash);

    /// <summary>Records that the device skipped a challenge. Never extends the expiry.</summary>
    public Result RecordUse(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            return Result.Failure(IdentityErrors.TrustedDeviceNotFound);
        }

        LastSeenAt = now;
        return Result.Success();
    }

    /// <summary>Forgets the device. Revoking an already-revoked device changes nothing.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
