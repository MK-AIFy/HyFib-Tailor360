using System.Text;

namespace Tailor360.Modules.Customers.Domain.Naming;

/// <summary>
/// Reduces a written name to the key a search and a duplicate score compare.
/// </summary>
/// <remarks>
/// <para>
/// One person's name reaches this shop written several ways. Lakshmi, Laxmi, Lakshmy and Lakshmee
/// are one customer; so are Shanthi and Santhi, Muthu and Muttu, Vijaya and Wijaya. Nobody at a
/// counter is going to try four spellings before deciding the customer is new, so the second record
/// gets created — and a duplicate customer is the exception EX-01 in
/// <c>docs/prd/exceptions.md</c>, whose only remedy is an irreversible, step-up merge. Collapsing
/// the spellings before they are compared is much cheaper than merging afterwards.
/// </para>
/// <para>
/// The output is <em>not</em> a name. It is never displayed, never printed, never sent to a
/// customer and never used as an identifier: it is a lower-case, unaccented, aggressively folded
/// string that exists so that two spellings of one name land on the same value and a trigram index
/// can rank the rest. The name the customer gave is stored separately and unaltered, and the
/// optional Tamil-script form is stored and searched on its own.
/// </para>
/// <para>
/// The folding is deliberately lossy in one direction only: it may bring two different people
/// together, and it may not keep one person apart from themselves. That trade is safe here because
/// nothing acts on the result by itself — a duplicate suspicion is shown to a person with its
/// reasons and requires an authorised decision (issue #26), and a search result is a list somebody
/// reads. It would not be safe if anything merged on it.
/// </para>
/// <para>
/// The rules and their worked examples are published in
/// <c>docs/customers/name-normalisation.md</c>, and the test vectors in that document are the unit
/// tests. Changing this table changes both.
/// </para>
/// </remarks>
public static class CustomerNameNormaliser
{
    /// <summary>The longest key the column holds; longer input is folded and then truncated.</summary>
    public const int MaximumLength = 200;

    /// <summary>
    /// The substitutions, in the order they are applied. Order matters: <c>tch</c> has to become
    /// <c>ch</c> before <c>ch</c> becomes <c>c</c>, and <c>sh</c> has to become <c>s</c> before
    /// <c>w</c> becomes <c>v</c> so that Bhuvaneshwari and Bhuvaneswari meet.
    /// </summary>
    public static IReadOnlyList<NameSubstitution> Substitutions { get; } =
    [
        new("x", "ks", false, "Laxmi and Lakshmi"),
        new("tch", "ch", false, "Kutchi and Kuchi"),
        new("chh", "ch", false, "Chhaya and Chaya"),
        new("zh", "l", false, "Tamizh and Tamil — the Tamil letter that has no Latin equivalent"),
        new("sh", "s", false, "Shanthi and Santhi, and Lakshmi and Laksmi"),
        new("ch", "c", false, "Chitra and Citra"),
        new("th", "t", false, "Kavitha and Kavita"),
        new("dh", "d", false, "Radha and Rada"),
        new("bh", "b", false, "Bhuvana and Buvana"),
        new("gh", "g", false, "Meghna and Megna"),
        new("kh", "k", false, "Lekha and Leka"),
        new("jh", "j", false, "Jhansi and Jansi"),
        new("ph", "p", false, "Sophia and Sopia"),
        new("w", "v", false, "Wijaya and Vijaya"),
        new("ee", "i", false, "Deepa and Dipa"),
        new("oo", "u", false, "Poornima and Purnima"),
        new("y", "i", true, "Lakshmy and Lakshmi — the interchangeable final vowel"),
    ];

    /// <summary>
    /// Folds a written name into its search key, or returns an empty string when nothing is left.
    /// </summary>
    /// <param name="name">The name as the customer gave it. Null and blank are accepted.</param>
    /// <returns>
    /// The key: lower-case ASCII letters and digits, single-spaced, at most
    /// <see cref="MaximumLength"/> characters. Empty when the input held no letter or digit — which
    /// is what a name written only in Tamil script produces, and why the native-script column is
    /// searched separately rather than through this.
    /// </returns>
    public static string Normalise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var folded = LatinFolding.Fold(name);
        var tokens = Tokenise(folded);

        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(folded.Length);

        foreach (var token in tokens)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Fold(token));
        }

        var key = builder.ToString().Trim();

        return key.Length <= MaximumLength ? key : key[..MaximumLength].TrimEnd();
    }

    /// <summary>
    /// Splits on anything that is not an ASCII letter or digit, lower-casing as it goes.
    /// </summary>
    /// <remarks>
    /// Dots, hyphens and apostrophes are separators rather than characters: <c>R.</c>, <c>R</c> and
    /// <c>R-</c> are one initial, and <c>D'Souza</c> and <c>D Souza</c> are one surname. Anything
    /// outside ASCII — Tamil script, for instance — is dropped here, which is deliberate: the native
    /// name has its own column and its own search, and transliterating it in code would invent a
    /// spelling nobody chose.
    /// </remarks>
    private static List<string> Tokenise(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                current.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Applies the substitutions to one word, then collapses runs of a repeated letter.
    /// </summary>
    /// <remarks>
    /// The collapse is the last step and it is what handles doubled consonants and doubled vowels
    /// together: Muttu and Mutu, Sellvam and Selvam, Kaala and Kala. It runs after the substitutions
    /// so that <c>ee</c> can become <c>i</c> rather than being flattened to <c>e</c> first.
    /// </remarks>
    private static string Fold(string token)
    {
        var value = token;

        foreach (var substitution in Substitutions)
        {
            value = substitution.WordFinalOnly
                ? ReplaceWordFinal(value, substitution.From, substitution.To)
                : value.Replace(substitution.From, substitution.To, StringComparison.Ordinal);

            if (value.Length == 0)
            {
                return string.Empty;
            }
        }

        return CollapseRepeats(value);
    }

    private static string ReplaceWordFinal(string value, string from, string to)
        => value.EndsWith(from, StringComparison.Ordinal)
            ? string.Concat(value.AsSpan(0, value.Length - from.Length), to)
            : value;

    private static string CollapseRepeats(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (builder.Length == 0 || builder[^1] != character)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
