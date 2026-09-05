using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Mfa;

/// <summary>
/// One account's time-based one-time-password authenticator. The shared secret never appears here in
/// the clear: the property holds the value after the platform's data protection has wrapped it, and
/// the domain treats it as an opaque string it can store and hand back but not read.
/// </summary>
/// <remarks>
/// <see cref="LastAcceptedStep"/> is the part that is easy to leave out and expensive to omit. A
/// time-based code is valid for a whole step — thirty seconds by default, and longer once a drift
/// window is allowed — so a code observed over someone's shoulder, or captured by a proxy in front of
/// the sign-in page, can be replayed inside that window. Recording the last step accepted and refusing
/// to accept it again turns a one-time password into one that is genuinely used once.
/// </remarks>
public sealed class TotpEnrolment
{
    /// <summary>The number of digits in a generated code.</summary>
    public const int DefaultDigits = 6;

    /// <summary>The length of one step, in seconds.</summary>
    public const int DefaultPeriodSeconds = 30;

    /// <summary>The longest protected secret the store accepts.</summary>
    public const int MaximumProtectedSecretLength = 2048;

    private TotpEnrolment()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private TotpEnrolment(
        Guid id,
        Guid userId,
        string protectedSecret,
        int digits,
        int periodSeconds,
        DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        ProtectedSecret = protectedSecret;
        Digits = digits;
        PeriodSeconds = periodSeconds;
        CreatedAt = now;
    }

    /// <summary>Identity of this enrolment.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account this enrolment belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The shared secret, wrapped by the platform's data protection. Never the raw secret.</summary>
    public string ProtectedSecret { get; private set; } = string.Empty;

    /// <summary>How many digits a code has.</summary>
    public int Digits { get; private set; } = DefaultDigits;

    /// <summary>How long one step lasts, in seconds.</summary>
    public int PeriodSeconds { get; private set; } = DefaultPeriodSeconds;

    /// <summary>When the secret was issued.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When a valid code first proved the holder had configured their authenticator.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>When a code from this authenticator was last accepted.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>
    /// The highest step whose code has been accepted. A code from this step or an earlier one is a
    /// replay and is refused however valid it would otherwise look.
    /// </summary>
    public long? LastAcceptedStep { get; private set; }

    /// <summary>True once a valid code has confirmed the enrolment.</summary>
    public bool IsConfirmed => ConfirmedAt is not null;

    /// <summary>Starts an enrolment from an already-protected secret.</summary>
    /// <param name="id">Identity of the enrolment, from <c>IIdGenerator</c>.</param>
    /// <param name="userId">The owning account.</param>
    /// <param name="protectedSecret">The shared secret after data protection has wrapped it.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="digits">Code length.</param>
    /// <param name="periodSeconds">Step length in seconds.</param>
    public static Result<TotpEnrolment> Begin(
        Guid id,
        Guid userId,
        string? protectedSecret,
        DateTimeOffset now,
        int digits = DefaultDigits,
        int periodSeconds = DefaultPeriodSeconds)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<TotpEnrolment>(IdentityErrors.Required("id"));
        }

        if (userId == Guid.Empty)
        {
            return Result.Failure<TotpEnrolment>(IdentityErrors.Required("userId"));
        }

        if (string.IsNullOrWhiteSpace(protectedSecret)
            || protectedSecret.Length > MaximumProtectedSecretLength)
        {
            return Result.Failure<TotpEnrolment>(IdentityErrors.Required("protectedSecret"));
        }

        if (digits is < 6 or > 8)
        {
            return Result.Failure<TotpEnrolment>(IdentityErrors.Required("digits"));
        }

        if (periodSeconds is < 15 or > 120)
        {
            return Result.Failure<TotpEnrolment>(IdentityErrors.Required("periodSeconds"));
        }

        return new TotpEnrolment(id, userId, protectedSecret!, digits, periodSeconds, now);
    }

    /// <summary>
    /// Confirms the enrolment with the step whose code the holder just typed. Confirmation also counts
    /// as a use, so the confirming code cannot immediately be replayed as a sign-in.
    /// </summary>
    public Result Confirm(long acceptedStep, DateTimeOffset now)
    {
        if (IsConfirmed)
        {
            return Result.Failure(IdentityErrors.MfaAlreadyEnrolled);
        }

        ConfirmedAt = now;
        LastAcceptedStep = acceptedStep;
        LastUsedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Records that a code from <paramref name="step"/> was accepted, refusing a step already used.
    /// </summary>
    public Result AcceptStep(long step, DateTimeOffset now)
    {
        if (!IsConfirmed)
        {
            return Result.Failure(IdentityErrors.MfaNotConfirmed);
        }

        if (LastAcceptedStep is { } last && step <= last)
        {
            return Result.Failure(IdentityErrors.TotpCodeReplayed);
        }

        LastAcceptedStep = step;
        LastUsedAt = now;

        return Result.Success();
    }
}
