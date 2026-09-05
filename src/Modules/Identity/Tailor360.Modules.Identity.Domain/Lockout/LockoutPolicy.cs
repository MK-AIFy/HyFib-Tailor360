using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Lockout;

/// <summary>
/// How an account responds to repeated failed sign-ins: nothing for the first few, then a lockout that
/// doubles with each further failure up to a ceiling.
/// </summary>
/// <remarks>
/// A fixed "five strikes and you are out for thirty minutes" is a denial-of-service tool pointed at
/// staff — anyone who knows a sign-in name can lock a cashier out of their till on a Saturday. Growing
/// the delay instead makes an online guessing attack hopeless within a handful of attempts (the sixth
/// failure already costs a minute, the eleventh half an hour) while a person who mistyped their
/// password twice waits seconds, not half a shift.
/// <para>
/// This is one of two throttles, not the only one. It bounds attempts against a single account; the
/// per-address rate-limit policy bounds an attacker spraying one password across many accounts, which
/// no per-account counter would ever notice.
/// </para>
/// </remarks>
/// <param name="Threshold">How many failures are tolerated before any lockout applies.</param>
/// <param name="BaseDuration">The lockout applied at the first failure past the threshold.</param>
/// <param name="MaximumDuration">The ceiling the doubling stops at.</param>
public sealed record LockoutPolicy(int Threshold, TimeSpan BaseDuration, TimeSpan MaximumDuration)
{
    /// <summary>How far the doubling is computed before the ceiling is applied regardless.</summary>
    private const int MaximumExponent = 20;

    /// <summary>The policy issue #23 sets when configuration says nothing else.</summary>
    public static LockoutPolicy Default { get; } =
        new(5, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));

    /// <summary>Builds a policy, refusing values that would disable the control.</summary>
    public static Result<LockoutPolicy> Create(
        int threshold,
        TimeSpan baseDuration,
        TimeSpan maximumDuration)
    {
        if (threshold < 1)
        {
            return Result.Failure<LockoutPolicy>(IdentityErrors.Required("threshold"));
        }

        if (baseDuration <= TimeSpan.Zero)
        {
            return Result.Failure<LockoutPolicy>(IdentityErrors.Required("baseDuration"));
        }

        if (maximumDuration < baseDuration)
        {
            return Result.Failure<LockoutPolicy>(IdentityErrors.Required("maximumDuration"));
        }

        return new LockoutPolicy(threshold, baseDuration, maximumDuration);
    }

    /// <summary>
    /// When the account may try again after <paramref name="failedCount"/> consecutive failures, or
    /// <see langword="null"/> while it is still under the threshold.
    /// </summary>
    public DateTimeOffset? ComputeLockoutEnd(int failedCount, DateTimeOffset now)
    {
        if (failedCount < Threshold)
        {
            return null;
        }

        var exponent = Math.Min(failedCount - Threshold, MaximumExponent);
        var scaled = BaseDuration * Math.Pow(2, exponent);
        var duration = scaled > MaximumDuration ? MaximumDuration : scaled;

        return now + duration;
    }
}
