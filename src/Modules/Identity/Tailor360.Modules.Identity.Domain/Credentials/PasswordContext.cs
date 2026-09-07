namespace Tailor360.Modules.Identity.Domain.Credentials;

/// <summary>
/// The account's own text, supplied to the password policy so that a password cannot simply be the
/// holder's name or sign-in address. Nothing here is stored or logged; it is read, compared and
/// discarded within one call.
/// </summary>
/// <param name="UserName">The sign-in name.</param>
/// <param name="Email">The email address.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
public sealed record PasswordContext(string? UserName, string? Email, string? DisplayName)
{
    /// <summary>An empty context, used when a password is checked outside the context of an account.</summary>
    public static PasswordContext None { get; } = new(null, null, null);

    /// <summary>
    /// The individual words a password must not contain. The email address is split at the <c>@</c> and
    /// the display name at its spaces, because "priya" out of "priya@example.test" and out of
    /// "Priya Raman" is the part someone actually reuses.
    /// </summary>
    public IEnumerable<string> Terms()
    {
        if (!string.IsNullOrWhiteSpace(UserName))
        {
            yield return UserName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(Email))
        {
            var localPart = Email.Split('@', 2)[0].Trim();
            if (localPart.Length > 0)
            {
                yield return localPart;
            }
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            yield break;
        }

        foreach (var word in DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            yield return word;
        }
    }
}
