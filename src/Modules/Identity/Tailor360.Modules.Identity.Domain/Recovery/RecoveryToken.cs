using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Recovery;

/// <summary>
/// The single-use, expiring credential a recovery email carries. Only its digest is stored, so the
/// table is not a list of working reset links.
/// </summary>
/// <remarks>
/// A recovery token is the strongest credential in the system for as long as it lives: whoever holds
/// it can set a password. Four properties keep that bounded, and all four are enforced here rather
/// than by whichever handler happens to use it.
/// <list type="bullet">
/// <item><description>It is stored as a digest, like every other server-generated secret in this
/// module, so a copy of the table is not a set of usable links.</description></item>
/// <item><description>It expires, and the ceiling on how long it may live is a constant rather than
/// configuration, because a token that is valid for a day is a password that is valid for a day.</description></item>
/// <item><description>It is spent exactly once, by <see cref="Consume"/>, which refuses a token that
/// already carries a consumption time.</description></item>
/// <item><description>It can be invalidated without being spent, which is what issuing a replacement
/// does to the tokens before it — a second "forgot my password" click must not leave two working
/// links in two inboxes.</description></item>
/// </list>
/// <para>
/// What this type deliberately does not carry is the requester's address or user agent. Knowing which
/// address asked for a reset would be useful about once a year and is personal data in a table that
/// otherwise holds none (<c>docs/nfr/data-classification.md</c>); the audit trail records the request.
/// </para>
/// <para>
/// Redeeming a token never alters the account's second factor. That rule lives in the handler because
/// it is a rule about what the handler must not do, but it is the reason this type exposes nothing
/// that could clear an enrolment.
/// </para>
/// </remarks>
public sealed class RecoveryToken
{
    /// <summary>
    /// The longest a recovery token may live, whatever configuration asks for. A reset link is a
    /// password with a timer on it, and an hour is already generous for someone reading their email.
    /// </summary>
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(1);

    /// <summary>The shortest lifetime that is usable at all, allowing for slow mail delivery.</summary>
    public static readonly TimeSpan MinimumLifetime = TimeSpan.FromMinutes(5);

    private RecoveryToken()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private RecoveryToken(
        Guid id,
        Guid userId,
        string tokenHash,
        RecoveryPurpose purpose,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        Purpose = purpose;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    /// <summary>Identity of this token.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account the token recovers.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The SHA-256 digest of the value in the link. Never the value itself.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>What the token entitles its holder to do.</summary>
    public RecoveryPurpose Purpose { get; private set; }

    /// <summary>When the token was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When it stops working of its own accord.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it was spent, if it has been.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>When it was withdrawn without being spent, if it was.</summary>
    public DateTimeOffset? InvalidatedAt { get; private set; }

    /// <summary>True once the token has been spent.</summary>
    public bool IsConsumed => ConsumedAt is not null;

    /// <summary>True when the token would still be accepted.</summary>
    public bool IsUsable(DateTimeOffset now)
        => ConsumedAt is null && InvalidatedAt is null && now < ExpiresAt;

    /// <summary>Issues a token from the digest of the value that will appear in the link.</summary>
    /// <param name="id">Identity of the token, from <c>IIdGenerator</c>.</param>
    /// <param name="userId">The account being recovered.</param>
    /// <param name="tokenHash">Lower-case hexadecimal SHA-256 digest of the link value.</param>
    /// <param name="purpose">What the token entitles its holder to do.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="lifetime">How long the token lives, capped at <see cref="MaximumLifetime"/>.</param>
    public static Result<RecoveryToken> Issue(
        Guid id,
        Guid userId,
        string? tokenHash,
        RecoveryPurpose purpose,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<RecoveryToken>(IdentityErrors.Required("id"));
        }

        if (userId == Guid.Empty)
        {
            return Result.Failure<RecoveryToken>(IdentityErrors.Required("userId"));
        }

        if (!HashedSecret.IsWellFormed(tokenHash))
        {
            return Result.Failure<RecoveryToken>(IdentityErrors.TokenNotHashed("recoveryToken"));
        }

        if (lifetime < MinimumLifetime || lifetime > MaximumLifetime)
        {
            return Result.Failure<RecoveryToken>(
                IdentityErrors.RecoveryTokenLifetimeInvalid(MinimumLifetime, MaximumLifetime));
        }

        return new RecoveryToken(id, userId, tokenHash!, purpose, now, now + lifetime);
    }

    /// <summary>True when the supplied digest is this token's, compared in fixed time.</summary>
    public bool Matches(string? candidateHash) => HashedSecret.Matches(TokenHash, candidateHash);

    /// <summary>
    /// Spends the token for the stated purpose. A token spent, withdrawn, expired or presented for the
    /// wrong purpose fails identically, so a caller learns nothing from which it was.
    /// </summary>
    public Result Consume(RecoveryPurpose purpose, DateTimeOffset now)
    {
        if (!IsUsable(now) || purpose != Purpose)
        {
            return Result.Failure(IdentityErrors.RecoveryTokenInvalid);
        }

        ConsumedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Withdraws an unspent token. Issuing a replacement does this to every outstanding token, so a
    /// second request never leaves two working links behind.
    /// </summary>
    public void Invalidate(DateTimeOffset now)
    {
        if (ConsumedAt is null && InvalidatedAt is null)
        {
            InvalidatedAt = now;
        }
    }
}
