using System.Security.Cryptography;
using System.Text;
using Tailor360.Modules.Identity.Application.Abstractions;

namespace Tailor360.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Generates recovery codes and reduces a typed one to the digest an account stores.
/// </summary>
/// <remarks>
/// Everything here is about a code surviving the round trip from a printer to a person to a keyboard,
/// which is where recovery codes usually fail rather than in their cryptography.
/// <list type="bullet">
/// <item><description><b>The alphabet omits what is misread.</b> No <c>0</c>/<c>O</c>, no
/// <c>1</c>/<c>I</c>/<c>L</c>, no <c>U</c> next to <c>V</c>. Twenty-six symbols remain, and ten
/// characters of them carry about forty-seven bits — far beyond guessing at any rate a
/// throttled endpoint allows.</description></item>
/// <item><description><b>What is printed is grouped, what is compared is not.</b> A code is shown as
/// two groups of five so it can be read aloud and copied; the digest is taken over the bare
/// characters, so it does not matter whether the person types the hyphen.</description></item>
/// <item><description><b>Case is folded up before hashing.</b> A code printed in capitals gets typed
/// in whatever the phone's keyboard offers.</description></item>
/// </list>
/// </remarks>
public sealed class RecoveryCodeService : IRecoveryCodeService
{
    /// <summary>The symbols a code is drawn from, with the ambiguous ones left out.</summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ2345";

    /// <summary>How many characters a code has, not counting the group separator.</summary>
    public const int CodeLength = 10;

    private const int GroupSize = 5;

    /// <inheritdoc />
    public IReadOnlyList<GeneratedRecoveryCode> Issue(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var codes = new List<GeneratedRecoveryCode>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // A repeat inside one sheet would make the aggregate refuse the whole issue, and a person
        // holding two identical codes has nine, not ten. At these odds the loop effectively never
        // turns twice; it is here so that "effectively never" is not the same as "never checked".
        while (codes.Count < count)
        {
            var code = GenerateOne();
            var digest = DigestOf(code);

            if (digest is not null && seen.Add(digest))
            {
                codes.Add(new GeneratedRecoveryCode(code, digest));
            }
        }

        return codes;
    }

    /// <inheritdoc />
    public string? DigestOf(string? submitted)
    {
        if (submitted is null)
        {
            return null;
        }

        var normalised = new StringBuilder(CodeLength);
        foreach (var character in submitted)
        {
            if (char.IsWhiteSpace(character) || character is '-' or '_')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(character);
            if (!Alphabet.Contains(upper, StringComparison.Ordinal))
            {
                return null;
            }

            if (normalised.Length == CodeLength)
            {
                return null;
            }

            normalised.Append(upper);
        }

        if (normalised.Length != CodeLength)
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalised.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateOne()
    {
        var printed = new StringBuilder(CodeLength + 1);

        for (var index = 0; index < CodeLength; index++)
        {
            if (index > 0 && index % GroupSize == 0)
            {
                printed.Append('-');
            }

            printed.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return printed.ToString();
    }
}
