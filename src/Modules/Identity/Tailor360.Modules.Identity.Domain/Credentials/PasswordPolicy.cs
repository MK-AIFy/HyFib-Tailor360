using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Credentials;

/// <summary>
/// What makes a password acceptable. The rules follow current guidance rather than the habits of the
/// last decade: length is the control that matters, composition rules ("one capital, one symbol") are
/// not imposed because they push people towards <c>Password1!</c>, and the checks that do exist are
/// the ones that block passwords an attacker would actually try first.
/// </summary>
/// <remarks>
/// Three checks live here because they need nothing but the candidate and the account: length, variety
/// and whether the password merely repeats the holder's own name or address. A fourth — whether the
/// password appears in a public breach list — needs a data source, so it sits behind a port in the
/// application layer and its failure is reported with <see cref="IdentityErrors.PasswordBreached"/>.
/// <para>
/// The check returns every failure at once rather than the first. A screen that reveals one rule per
/// attempt teaches the person to guess at the rules, which is a worse experience and produces weaker
/// passwords.
/// </para>
/// </remarks>
public sealed record PasswordPolicy
{
    /// <summary>
    /// The shortest password this system will ever accept, whatever configuration says. Twelve
    /// characters is the floor issue #23 sets; configuration may raise it and may not lower it.
    /// </summary>
    public const int AbsoluteMinimumLength = 12;

    /// <summary>The longest password the hasher is asked to process.</summary>
    public const int AbsoluteMaximumLength = 1024;

    /// <summary>How many different characters a password must contain.</summary>
    public const int MinimumDistinctCharacters = 5;

    /// <summary>The shortest run of account text that counts as "contains your own name".</summary>
    public const int MinimumAccountTermLength = 4;

    private PasswordPolicy(int minimumLength, int maximumLength)
    {
        MinimumLength = minimumLength;
        MaximumLength = maximumLength;
    }

    /// <summary>The configured minimum length, never below <see cref="AbsoluteMinimumLength"/>.</summary>
    public int MinimumLength { get; }

    /// <summary>The configured maximum length.</summary>
    public int MaximumLength { get; }

    /// <summary>The policy applied when configuration says nothing else.</summary>
    public static PasswordPolicy Default { get; } = new(AbsoluteMinimumLength, 256);

    /// <summary>Builds a policy, refusing one weaker than the floor.</summary>
    public static Result<PasswordPolicy> Create(int minimumLength, int maximumLength)
    {
        if (minimumLength < AbsoluteMinimumLength)
        {
            return Result.Failure<PasswordPolicy>(IdentityErrors.PasswordTooShort(AbsoluteMinimumLength));
        }

        if (maximumLength > AbsoluteMaximumLength || maximumLength < minimumLength)
        {
            return Result.Failure<PasswordPolicy>(IdentityErrors.PasswordTooLong(AbsoluteMaximumLength));
        }

        return new PasswordPolicy(minimumLength, maximumLength);
    }

    /// <summary>Checks a candidate password and reports every rule it breaks.</summary>
    /// <param name="candidate">The proposed password. Never logged, never stored.</param>
    /// <param name="account">The account's own text, which a password must not simply repeat.</param>
    public PasswordPolicyResult Check(string? candidate, PasswordContext account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var failures = new List<Error>();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            failures.Add(IdentityErrors.Required("password"));
            return new PasswordPolicyResult(failures);
        }

        if (candidate.Length < MinimumLength)
        {
            failures.Add(IdentityErrors.PasswordTooShort(MinimumLength));
        }

        if (candidate.Length > MaximumLength)
        {
            failures.Add(IdentityErrors.PasswordTooLong(MaximumLength));

            // Everything below scans the candidate; an over-long one is rejected before that work.
            return new PasswordPolicyResult(failures);
        }

        if (DistinctCharacterCount(candidate) < MinimumDistinctCharacters)
        {
            failures.Add(IdentityErrors.PasswordTooRepetitive(MinimumDistinctCharacters));
        }

        if (RepeatsAccountText(candidate, account))
        {
            failures.Add(IdentityErrors.PasswordContainsAccountDetail);
        }

        return new PasswordPolicyResult(failures);
    }

    private static int DistinctCharacterCount(string candidate)
    {
        var seen = new HashSet<char>();
        foreach (var character in candidate)
        {
            seen.Add(character);
            if (seen.Count >= MinimumDistinctCharacters)
            {
                return seen.Count;
            }
        }

        return seen.Count;
    }

    private static bool RepeatsAccountText(string candidate, PasswordContext account)
    {
        foreach (var term in account.Terms())
        {
            if (term.Length >= MinimumAccountTermLength
                && candidate.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
