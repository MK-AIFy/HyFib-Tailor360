using System.Text.RegularExpressions;

namespace Tailor360.ArchitectureTests;

/// <summary>
/// Applies a pattern to source text and reports the lines that match, skipping comments so that a rule
/// quoted in documentation is not reported as a breach of itself. Both the real rules and the negative
/// control run through this method, so the control proves the detector that is actually used.
/// </summary>
public static class SourceScanner
{
    /// <summary>Finds matching lines in the supplied files.</summary>
    /// <param name="files">Pairs of display path and file content.</param>
    /// <param name="pattern">The pattern that identifies a violation.</param>
    /// <param name="sanctionedFileNames">File names where the pattern is the sanctioned implementation.</param>
    public static IReadOnlyList<string> Scan(
        IEnumerable<(string Path, string Text)> files,
        Regex pattern,
        IReadOnlyList<string> sanctionedFileNames)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(sanctionedFileNames);

        var violations = new List<string>();

        foreach (var (path, text) in files)
        {
            if (sanctionedFileNames.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            {
                continue;
            }

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (pattern.IsMatch(line))
                {
                    violations.Add($"{path}:{i + 1}: {line.Trim()}");
                }
            }
        }

        return violations;
    }
}
