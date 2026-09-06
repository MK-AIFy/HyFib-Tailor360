using System.Globalization;

namespace Tailor360.UnitTests.Security;

/// <summary>
/// Reads the owner-approved permission matrix as data.
/// </summary>
/// <remarks>
/// <para>
/// Only the text between paired <c>&lt;!-- matrix:name --&gt;</c> markers is read, so the document can
/// carry as much explanation as its readers need without any of it reaching a test. Inside a marked
/// block there must be exactly one pipe table, and every row must have the same number of cells as the
/// header — a malformed table throws rather than yielding nothing, because a table that silently
/// yielded zero rows would satisfy every "every row must…" assertion in the suite.
/// </para>
/// </remarks>
public sealed class PermissionMatrixDocument
{
    private readonly Dictionary<string, MatrixTable> _sections;

    private PermissionMatrixDocument(Dictionary<string, MatrixTable> sections) => _sections = sections;

    /// <summary>The path the document is read from, relative to the repository root.</summary>
    public const string RelativePath = "docs/security/permission-matrix.md";

    /// <summary>Reads the matrix that ships in this repository.</summary>
    public static PermissionMatrixDocument Load() => Parse(File.ReadAllText(FullPath));

    /// <summary>The absolute path of the document in this working tree.</summary>
    public static string FullPath => Path.Combine(RepositoryRoot(), RelativePath);

    /// <summary>Reads a matrix from markdown, for the parser's own tests.</summary>
    /// <exception cref="InvalidOperationException">A marked block is unterminated or malformed.</exception>
    public static PermissionMatrixDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var sections = new Dictionary<string, MatrixTable>(StringComparer.Ordinal);
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var name = OpeningMarker(lines[index]);
            if (name is null)
            {
                continue;
            }

            var closing = $"<!-- /matrix:{name} -->";
            var end = Array.FindIndex(lines, index + 1, line => line.Trim() == closing);
            if (end < 0)
            {
                throw new InvalidOperationException(
                    $"The '{name}' block in the permission matrix is never closed with '{closing}'.");
            }

            if (!sections.TryAdd(name, ReadTable(name, lines[(index + 1)..end])))
            {
                throw new InvalidOperationException(
                    $"The permission matrix declares the '{name}' block more than once.");
            }

            index = end;
        }

        return new PermissionMatrixDocument(sections);
    }

    /// <summary>The rows of one marked block.</summary>
    /// <exception cref="InvalidOperationException">The document declares no such block.</exception>
    public MatrixTable Section(string name)
        => _sections.TryGetValue(name, out var table)
            ? table
            : throw new InvalidOperationException(
                $"The permission matrix has no '{name}' block. The blocks it has are: "
                + string.Join(", ", _sections.Keys.Order(StringComparer.Ordinal)) + ".");

    private static string? OpeningMarker(string line)
    {
        var trimmed = line.Trim();
        const string Prefix = "<!-- matrix:";
        const string Suffix = " -->";

        return trimmed.StartsWith(Prefix, StringComparison.Ordinal)
               && trimmed.EndsWith(Suffix, StringComparison.Ordinal)
            ? trimmed[Prefix.Length..^Suffix.Length]
            : null;
    }

    private static MatrixTable ReadTable(string name, IReadOnlyList<string> block)
    {
        var rows = block
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .ToArray();

        if (rows.Length < 3)
        {
            throw new InvalidOperationException(
                $"The '{name}' block must hold a pipe table with a header, a separator and at least one "
                + $"row; it has {rows.Length} table lines.");
        }

        var headers = Cells(rows[0]);
        if (!Cells(rows[1]).All(cell => cell.Length > 0 && cell.All(c => c is '-' or ':')))
        {
            throw new InvalidOperationException(
                $"The second line of the '{name}' block is not a table separator.");
        }

        var parsed = new List<MatrixRow>();
        foreach (var row in rows.Skip(2))
        {
            var cells = Cells(row);
            if (cells.Length != headers.Length)
            {
                throw new InvalidOperationException(
                    $"A row of the '{name}' block has {cells.Length} cells and the header has "
                    + $"{headers.Length}. The row begins: {Excerpt(row)}");
            }

            parsed.Add(new MatrixRow(name, headers, cells));
        }

        return new MatrixTable(name, parsed);
    }

    private static string[] Cells(string row)
    {
        var trimmed = row.Trim();
        var inner = trimmed.Trim('|');
        return [.. inner.Split('|').Select(cell => cell.Trim())];
    }

    private static string Excerpt(string row) => row.Length <= 60 ? row : row[..60] + "…";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HyFib.Tailor360.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root: no HyFib.Tailor360.slnx above " + AppContext.BaseDirectory);
    }
}

/// <summary>The rows of one marked block of the permission matrix.</summary>
/// <param name="Name">The block's name.</param>
/// <param name="Rows">Its rows, in document order.</param>
public sealed record MatrixTable(string Name, IReadOnlyList<MatrixRow> Rows);

/// <summary>One row of a marked block, addressed by column heading rather than by position.</summary>
/// <remarks>
/// Addressing by heading is what lets the document grow a column — a rationale, an issue number — without
/// every test having to be renumbered, and what makes a test fail loudly when a column is renamed rather
/// than quietly reading the wrong one.
/// </remarks>
public sealed class MatrixRow
{
    private readonly string _section;
    private readonly string[] _headers;
    private readonly string[] _cells;

    internal MatrixRow(string section, string[] headers, string[] cells)
    {
        _section = section;
        _headers = headers;
        _cells = cells;
    }

    /// <summary>The raw text of a cell, with any wrapping back-ticks removed.</summary>
    /// <exception cref="InvalidOperationException">The block has no such column.</exception>
    public string Text(string column)
    {
        var index = Array.FindIndex(_headers, header => string.Equals(header, column, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"The '{_section}' block has no column '{column}'. Its columns are: "
                + string.Join(", ", _headers) + ".");
        }

        return Unwrap(_cells[index]);
    }

    /// <summary>A <c>yes</c> or <c>no</c> cell.</summary>
    /// <exception cref="InvalidOperationException">The cell says something else.</exception>
    public bool Flag(string column)
    {
        var value = Text(column);
        return value switch
        {
            "yes" => true,
            "no" => false,
            _ => throw new InvalidOperationException(
                $"The '{column}' cell of the '{_section}' block must be 'yes' or 'no'; it is '{value}'."),
        };
    }

    /// <summary>An integer cell.</summary>
    /// <exception cref="InvalidOperationException">The cell is not a number.</exception>
    public int Number(string column)
    {
        var value = Text(column);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"The '{column}' cell of the '{_section}' block must be a whole number; it is '{value}'.");
    }

    /// <summary>A comma-separated list cell. An em dash means "none".</summary>
    public IReadOnlyList<string> List(string column)
    {
        var value = Text(column);
        return value is "—" or ""
            ? []
            : [.. value.Split(',').Select(item => Unwrap(item.Trim())).Where(item => item.Length > 0)];
    }

    private static string Unwrap(string cell) => cell.Trim().Trim('`').Trim();
}
