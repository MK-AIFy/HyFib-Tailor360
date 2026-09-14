using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Tailor360.IntegrationTests.Billing;

// A copy of tests/Tailor360.ContractTests/Adapters/PdfText.cs, kept in step with it by hand.
// There is no shared test project to put it in, and adding one is a repository-layout change rather
// than this slice's (#326): the contract tier proves the renderer against the accountant's golden
// master, this tier proves the *stored* document against the persisted snapshot, and the two tiers
// deliberately share no assembly. If a third caller appears, that is the moment to make the project.

/// <summary>
/// Reads a rendered PDF back as text, so an assertion reads the <em>page</em> rather than the model the
/// renderer was handed. Deliberately not a PDF library: what words are on it, what text lines they form
/// in reading order, what its information dictionary says, and what amounts it prints. Everything else a
/// test wants to know, it asks of these.
/// </summary>
internal static partial class PdfText
{
    /// <summary>Baselines within this many points are one text line; QuestPDF puts a table row's cells on exactly one.</summary>
    private const double BaselineTolerance = 2.0;

    /// <summary>The words of every page, in reading order: page by page, top to bottom, then left to right.</summary>
    public static IReadOnlyList<string> Words(byte[] pdf)
        => [.. Lines(pdf).SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))];

    /// <summary>The words grouped into the text lines they share, in reading order.</summary>
    public static IReadOnlyList<string> Lines(byte[] pdf)
    {
        // Measured against the first word of the line rather than the one before it, so a column of words
        // each a little lower than the last never chains into one line; and never bucketed by a rounded
        // baseline, which splits a row whenever its block happens to land on a bucket's edge.
        var lines = new List<string>();
        var line = new List<PlacedWord>();

        foreach (var word in Placed(pdf))
        {
            if (line.Count > 0 && (line[0].Page != word.Page || Math.Abs(line[0].Baseline - word.Baseline) > BaselineTolerance))
            {
                lines.Add(Join(line));
                line.Clear();
            }

            line.Add(word);
        }

        if (line.Count > 0)
        {
            lines.Add(Join(line));
        }

        return lines;
    }

    /// <summary>The document information dictionary, plus the catalogue's natural language under <c>Lang</c>.</summary>
    public static IReadOnlyDictionary<string, string> Metadata(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        var catalogue = document.Structure.Catalog.CatalogDictionary;
        var language = catalogue.TryGet(NameToken.Create("Lang"), out StringToken token) && token is not null ? token.Data : string.Empty;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Title"] = document.Information.Title ?? string.Empty,
            ["Author"] = document.Information.Author ?? string.Empty,
            ["Subject"] = document.Information.Subject ?? string.Empty,
            ["Creator"] = document.Information.Creator ?? string.Empty,
            ["Producer"] = document.Information.Producer ?? string.Empty,
            ["Lang"] = language,
        };
    }

    /// <summary>
    /// The font families the document embeds, read from its font descriptors. QuestPDF draws every glyph
    /// as a Type3 procedure, so the per-letter font a reader reports is the useless name "Type3" and the
    /// only place the face survives is the descriptor — which is where a test must look to tell the
    /// registered Noto faces from whatever the host machine happened to substitute.
    /// </summary>
    public static IReadOnlyCollection<string> FontFamilies(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        var families = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var reference in document.Structure.CrossReferenceTable.ObjectOffsets.Keys)
        {
            if (document.Structure.GetObject(reference).Data is DictionaryToken dictionary
                && dictionary.TryGet(NameToken.Create("FontFamily"), out StringToken family)
                && family is not null)
            {
                families.Add(family.Data);
            }
        }

        return families;
    }

    /// <summary>The amount printed beside <paramref name="label"/>, read from the page rather than a model.</summary>
    public static decimal? AmountBeside(byte[] pdf, string label) => AmountBeside(Lines(pdf), label);

    /// <summary>
    /// The same question asked of lines already read, so one reading answers many. The line must carry the
    /// label and one amount and nothing else, which is the shape of a totals row; a label that merely
    /// opens a longer line — <c>CGST 2.5% ₹14.50</c> beneath a table row — is a different figure and is
    /// deliberately not answered here.
    /// </summary>
    public static decimal? AmountBeside(IReadOnlyList<string> lines, string label)
    {
        var prefix = label + " ";
        var printed = lines
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..])
            .FirstOrDefault(IsAmount);

        return printed is null ? null : Amount(printed);
    }

    /// <summary>The amounts printed on a text line, in the order the line prints them.</summary>
    public static IReadOnlyList<decimal> AmountsOn(string line)
        => [.. line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(IsAmount).Select(Amount)];

    /// <summary>True when a word is an amount in the document's own money format.</summary>
    public static bool IsAmount(string word) => Money().IsMatch(word);

    /// <summary>A printed amount as the number it stands for, to the paisa.</summary>
    public static decimal Amount(string printed)
        => decimal.Parse(
            printed.Replace("₹", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture);

    /// <summary>An amount as <c>DocumentModel.Money</c> writes one in rupees, sign and lakh grouping included.</summary>
    [GeneratedRegex(@"^-?₹[0-9,]+\.[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex Money();

    private static string Join(List<PlacedWord> line)
        => string.Join(' ', line.OrderBy(word => word.Left).Select(word => word.Text));

    private static List<PlacedWord> Placed(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));

        return [.. document.GetPages()
            .SelectMany(page => page.GetWords().Select(word =>
                new PlacedWord(page.Number, word.BoundingBox.Bottom, word.BoundingBox.Left, word.Text)))
            .OrderBy(word => word.Page)
            .ThenByDescending(word => word.Baseline)
            .ThenBy(word => word.Left)];
    }

    /// <summary>A word and where it sits: its page, its baseline, and how far from the left edge.</summary>
    private sealed record PlacedWord(int Page, double Baseline, double Left, string Text);
}
