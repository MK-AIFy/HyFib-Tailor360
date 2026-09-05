namespace Tailor360.Modules.Identity.Application.Authentication;

/// <summary>
/// Folds what somebody typed into the sign-in box to the one form the rest of the system uses.
/// </summary>
/// <remarks>
/// <para>
/// Staff sign in with whichever of their sign-in name and their address they remember, and the store
/// keeps the first folded to lower case and the second to upper. Two places used to know that: the
/// directory that looks the account up, and the endpoint that counts attempts against it. The day they
/// disagreed, the symptoms were an account nobody could sign in to and — because <c>alice</c> and
/// <c>alice@shop.example</c> counted as two different things — an attacker with twice the attempts the
/// throttle was configured to allow.
/// </para>
/// <para>
/// The alias problem does not go away by folding case alone: the two forms are still different strings.
/// What closes it is the second check the sign-in handler makes once the directory has answered, keyed
/// on the account identifier, which both forms resolve to.
/// </para>
/// </remarks>
public static class SignInIdentifier
{
    /// <summary>
    /// The stored form of what was typed, or null when nothing usable was. An address is recognised by
    /// the <c>@</c> rather than by asking the person which they typed: a form that made them choose
    /// first would be one more thing to get wrong at a counter.
    /// </summary>
    /// <param name="identifier">The sign-in name or address as typed.</param>
    public static string? Normalise(string? identifier)
    {
        var typed = identifier?.Trim();
        if (string.IsNullOrEmpty(typed))
        {
            return null;
        }

        return typed.Contains('@', StringComparison.Ordinal)
            ? typed.ToUpperInvariant()
            : typed.ToLowerInvariant();
    }

    /// <summary>True when the value names an address rather than a sign-in name.</summary>
    /// <param name="identifier">The normalised value.</param>
    public static bool IsEmailAddress(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return identifier.Contains('@', StringComparison.Ordinal);
    }
}
