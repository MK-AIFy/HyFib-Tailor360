using System.Globalization;
using System.Text;

namespace Tailor360.Modules.Customers.Domain.Naming;

/// <summary>
/// Folds accented Latin letters onto the letters underneath them.
/// </summary>
/// <remarks>
/// <para>
/// This exists instead of <c>string.Normalize(NormalizationForm.FormD)</c> because the whole
/// solution builds with <c>InvariantGlobalization</c> (<c>Directory.Build.props</c>), and under
/// invariant globalization Unicode normalisation is not available: the call returns the string
/// unchanged rather than failing, so an accented letter would survive to the tokeniser, be rejected
/// as "not an ASCII letter", and split the name in half. Revathi typed with an acute accent would
/// have been folded to <c>r vati</c> and never found again. The failure is silent, which is why the
/// table is written out rather than delegated.
/// </para>
/// <para>
/// The two tables are parallel strings rather than a dictionary so that a reviewer can see the whole
/// mapping at once and check it by eye. <see cref="TablesAgree"/> is asserted by a unit test, because
/// a table one character out of step would fold half the alphabet to the wrong letter and still
/// build.
/// </para>
/// <para>
/// The set covers the Latin-1 and Latin Extended-A letters a name reaches this shop with, plus the
/// dot-below and macron letters of scholarly Indic transliteration (<c>ṇ</c> n-with-dot-below
/// and its neighbours), which is how a name copied from an identity document sometimes arrives.
/// Anything outside the set is left alone and the tokeniser treats it as a separator — which is the
/// right answer for Tamil script, whose own column is searched directly.
/// </para>
/// </remarks>
public static class LatinFolding
{
    /// <summary>
    /// Accented letters, and the plain letter each becomes. Kept as two parallel strings so that the
    /// pairing is visible on one screen and cannot drift.
    /// </summary>
    private const string Accented =
        "ÀÁÂÃÄÅĀĂĄẠ"
        + "àáâãäåāăąạ"
        + "ÇĆĈĊČçćĉċč"
        + "ĎĐḌďđḍ"
        + "ÈÉÊËĒĔĖĘĚ"
        + "èéêëēĕėęě"
        + "ĜĞĠĢĝğġģ"
        + "ĤḢḤĥḣḥ"
        + "ÌÍÎÏĪĬĮİ"
        + "ìíîïīĭįı"
        + "ĴĵĶḲķḳ"
        + "ĹĻĽŁḶĺļľłḷ"
        + "ṀṂṁṃ"
        + "ÑŃŅŇṄṆñńņňṅṇ"
        + "ÒÓÔÕÖØŌŎŐ"
        + "òóôõöøōŏő"
        + "ṖṗŔŖŘṚŕŗřṛ"
        + "ŚŜŞŠṢśŝşšṣ"
        + "ŢŤṬţťṭ"
        + "ÙÚÛÜŪŬŮŰŲ"
        + "ùúûüūŭůűų"
        + "ẂẄẃẅ"
        + "ÝŶŸýŷÿ"
        + "ŹŻŽźżž";

    /// <summary>The plain letter each entry of <see cref="Accented"/> folds to, in the same order.</summary>
    private const string Plain =
        "AAAAAAAAAA"
        + "aaaaaaaaaa"
        + "CCCCCccccc"
        + "DDDddd"
        + "EEEEEEEEE"
        + "eeeeeeeee"
        + "GGGGgggg"
        + "HHHhhh"
        + "IIIIIIII"
        + "iiiiiiii"
        + "JjKKkk"
        + "LLLLLlllll"
        + "MMmm"
        + "NNNNNNnnnnnn"
        + "OOOOOOOOO"
        + "ooooooooo"
        + "PpRRRRrrrr"
        + "SSSSSsssss"
        + "TTTttt"
        + "UUUUUUUUU"
        + "uuuuuuuuu"
        + "WWww"
        + "YYYyyy"
        + "ZZZzzz";

    /// <summary>Letters that fold to more than one letter, and so cannot live in the parallel strings.</summary>
    private static readonly (char From, string To)[] Expansions =
    [
        ('Æ', "AE"),
        ('æ', "ae"),
        ('Œ', "OE"),
        ('œ', "oe"),
        ('ß', "ss"),
        ('Þ', "TH"),
        ('þ', "th"),
        ('Ð', "D"),
        ('ð', "d"),
    ];

    /// <summary>
    /// Returns the value with accented letters replaced by plain ones and combining marks dropped.
    /// </summary>
    /// <param name="value">Any text. Characters outside the table are returned unchanged.</param>
    /// <returns>The folded text.</returns>
    public static string Fold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            // Input that arrived already decomposed — a base letter followed by its own accent, which
            // is what some phone keyboards produce — loses the accent here.
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var index = Accented.IndexOf(character, StringComparison.Ordinal);
            if (index >= 0)
            {
                builder.Append(Plain[index]);
                continue;
            }

            var expansion = FindExpansion(character);
            if (expansion is not null)
            {
                builder.Append(expansion);
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Whether the two tables are the same length. Asserted by a unit test, not at run time.</summary>
    public static bool TablesAgree => Accented.Length == Plain.Length;

    private static string? FindExpansion(char character)
    {
        foreach (var (from, to) in Expansions)
        {
            if (from == character)
            {
                return to;
            }
        }

        return null;
    }
}
