using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Mfa;

/// <summary>
/// One printed recovery code, stored only as its hash and usable exactly once.
/// </summary>
/// <remarks>
/// Both halves of that sentence are enforced here rather than left to the caller. The constructor
/// accepts nothing but a well-formed SHA-256 digest, so there is no path by which a legible code
/// reaches the table; and <see cref="Consume"/> refuses a code that already carries a consumption
/// time, so "single use" holds even if a caller loops over the same code twice. A code that failed to
/// be consumed is a failure the caller must handle, never a silent no-op.
/// </remarks>
public sealed class RecoveryCode
{
    private RecoveryCode()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private RecoveryCode(Guid id, Guid userId, string codeHash, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        CodeHash = codeHash;
        CreatedAt = now;
    }

    /// <summary>Identity of this code.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account this code belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The SHA-256 digest of the printed code. Never the code itself.</summary>
    public string CodeHash { get; private set; } = string.Empty;

    /// <summary>When the code was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the code was spent, if it has been.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>True once the code has been spent.</summary>
    public bool IsConsumed => ConsumedAt is not null;

    /// <summary>Creates a code from its digest.</summary>
    /// <param name="id">Identity of the code, from <c>IIdGenerator</c>.</param>
    /// <param name="userId">The owning account.</param>
    /// <param name="codeHash">Lower-case hexadecimal SHA-256 digest of the printed code.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    public static Result<RecoveryCode> Create(Guid id, Guid userId, string? codeHash, DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<RecoveryCode>(IdentityErrors.Required("id"));
        }

        if (userId == Guid.Empty)
        {
            return Result.Failure<RecoveryCode>(IdentityErrors.Required("userId"));
        }

        if (!HashedSecret.IsWellFormed(codeHash))
        {
            return Result.Failure<RecoveryCode>(IdentityErrors.RecoveryCodeNotHashed);
        }

        return new RecoveryCode(id, userId, codeHash!, now);
    }

    /// <summary>True when the supplied digest is this code's, compared in fixed time.</summary>
    public bool Matches(string? candidateHash) => HashedSecret.Matches(CodeHash, candidateHash);

    /// <summary>Spends the code. Fails if it has already been spent.</summary>
    public Result Consume(DateTimeOffset now)
    {
        if (IsConsumed)
        {
            return Result.Failure(IdentityErrors.RecoveryCodeInvalid);
        }

        ConsumedAt = now;
        return Result.Success();
    }
}
