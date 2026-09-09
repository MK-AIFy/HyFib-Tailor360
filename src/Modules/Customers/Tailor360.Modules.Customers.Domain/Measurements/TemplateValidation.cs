namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// Everything that must be true of a template version before it may be published.
/// </summary>
/// <remarks>
/// <para>
/// The checks are the ones listed in <c>docs/prd/measurement-templates.md</c> section 6, and they exist because a
/// published version is immutable: a mistake found afterwards cannot be corrected in place, only superseded, and
/// every measurement captured in the meantime carries it. So this runs at the last moment it is still cheap.
/// </para>
/// <para>
/// <strong>Every finding is reported, not the first.</strong> An administrator fixing a template wants the whole
/// list; failing on the first problem turns one review into six round trips. Errors refuse publication, warnings
/// are said and do not.
/// </para>
/// <para>
/// One check from section 6 is deliberately absent: "a missing or retired category or service reference". The
/// reference runs the other way — a catalogue service type carries the template version, not the reverse
/// (section 1) — so it is checked where it lives, by the dependency validator this module registers with the
/// Catalog module. Duplicating it here would mean Customers reading the catalogue's tables, which is the boundary
/// violation the whole registration mechanism exists to avoid.
/// </para>
/// </remarks>
public static class TemplateValidation
{
    /// <summary>Checks a version and reports everything wrong with it.</summary>
    /// <param name="version">The version to check.</param>
    /// <returns>The findings, empty when the version is ready.</returns>
    public static IReadOnlyList<TemplateFinding> Validate(TemplateVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var findings = new List<TemplateFinding>();

        if (version.Fields.Count == 0)
        {
            findings.Add(new TemplateFinding(
                TemplateFindingSeverity.Error,
                "version-has-no-fields",
                "This version has no fields, so it would capture nothing.",
                "fields"));

            return findings;
        }

        CheckDuplicateKeys(version, findings);
        CheckFields(version, findings);
        CheckRules(version, findings);
        CheckCycles(version, findings);

        return findings;
    }

    /// <summary>Whether any finding refuses publication.</summary>
    /// <param name="findings">The findings.</param>
    /// <returns>True when at least one is an error.</returns>
    public static bool HasErrors(IEnumerable<TemplateFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return findings.Any(finding => finding.Severity == TemplateFindingSeverity.Error);
    }

    private static void CheckDuplicateKeys(TemplateVersion version, List<TemplateFinding> findings)
    {
        var duplicates = version.Fields
            .GroupBy(field => field.Key.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);

        foreach (var duplicate in duplicates)
        {
            findings.Add(new TemplateFinding(
                TemplateFindingSeverity.Error,
                "duplicate-field-key",
                $"'{duplicate.Key}' is used by more than one field. A key identifies exactly one field, because it "
                + "is what a captured value is filed under.",
                $"fields[{duplicate.Key}].key"));
        }
    }

    private static void CheckFields(TemplateVersion version, List<TemplateFinding> findings)
    {
        foreach (var field in version.Fields)
        {
            var key = field.Key.Value;

            // Re-run the field-level rules rather than trusting that they were checked on the way in. This is the
            // report an administrator publishes against, and a report that assumes an earlier check ran is a report
            // that says nothing when that check is the one that was missed.
            var bands = field.Bands.Validate(field.Key);

            if (bands.IsFailure)
            {
                findings.Add(new TemplateFinding(
                    TemplateFindingSeverity.Error, bands.Error.Code, bands.Error.Message, $"fields[{key}].bands"));
            }

            if (!field.IsChoice)
            {
                var precision = field.Precision.Validate();

                if (precision.IsFailure)
                {
                    findings.Add(new TemplateFinding(
                        TemplateFindingSeverity.Error,
                        precision.Error.Code,
                        precision.Error.Message,
                        $"fields[{key}].precision"));
                }

                if (field.CanonicalUnit == CanonicalUnit.Millimetre && field.DisplayUnits.Count == 0)
                {
                    findings.Add(new TemplateFinding(
                        TemplateFindingSeverity.Error,
                        "field-has-no-display-unit",
                        $"'{key}' is a length and declares no step in inches or centimetres, so there is no unit "
                        + "anybody could enter it in.",
                        $"fields[{key}].precision"));
                }
            }

            if (field.IsChoice && field.Options.Count == 0)
            {
                findings.Add(new TemplateFinding(
                    TemplateFindingSeverity.Error,
                    "choice-field-has-no-options",
                    $"'{key}' is chosen from a list and the list is empty.",
                    $"fields[{key}].options"));
            }

            if (field.IsRequired && field.Rule is { } rule && rule.HidesUnconditionally)
            {
                findings.Add(new TemplateFinding(
                    TemplateFindingSeverity.Error,
                    "required-field-never-shown",
                    $"'{key}' is required and its rule always hides it, so a capture could never be completed.",
                    $"fields[{key}].rule"));
            }

            if (field.DiagramKey is null && field.DiagramMediaId is null)
            {
                findings.Add(new TemplateFinding(
                    TemplateFindingSeverity.Warning,
                    "field-has-no-diagram",
                    $"'{key}' has no diagram. A tailor reading the field for the first time has only the help text "
                    + "to go on.",
                    $"fields[{key}].diagramKey"));
            }

            if (string.IsNullOrWhiteSpace(field.LabelTamil))
            {
                findings.Add(new TemplateFinding(
                    TemplateFindingSeverity.Warning,
                    "field-has-no-tamil-label",
                    $"'{key}' has no Tamil label, so it reads in English on a Tamil sheet (OD-MEA-10).",
                    $"fields[{key}].labelTamil"));
            }
        }
    }

    private static void CheckRules(TemplateVersion version, List<TemplateFinding> findings)
    {
        var keys = version.Fields.Select(field => field.Key.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var field in version.Fields.Where(field => field.Rule is not null))
        {
            var key = field.Key.Value;
            var rule = field.Rule!;

            var shape = rule.Validate(field.Key);

            if (shape.IsFailure)
            {
                findings.Add(new TemplateFinding(
                    TemplateFindingSeverity.Error, shape.Error.Code, shape.Error.Message, $"fields[{key}].rule"));
            }

            foreach (var referenced in rule.ReferencedFieldKeys.Where(name => !keys.Contains(name)))
            {
                findings.Add(new TemplateFinding(
                    TemplateFindingSeverity.Error,
                    "unknown-rule-field",
                    $"The rule on '{key}' reads '{referenced}', which is not a field of this version.",
                    $"fields[{key}].rule"));
            }
        }
    }

    private static void CheckCycles(TemplateVersion version, List<TemplateFinding> findings)
    {
        var graph = version.Fields
            .Where(field => field.Rule is not null)
            .ToDictionary(
                field => field.Key.Value,
                field => field.Rule!.ReferencedFieldKeys.Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var settled = new HashSet<string>(StringComparer.Ordinal);
        var walking = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var start in graph.Keys)
        {
            Walk(start, graph, settled, walking, path, reported, findings);
        }
    }

    private static void Walk(
        string key,
        Dictionary<string, string[]> graph,
        HashSet<string> settled,
        HashSet<string> walking,
        List<string> path,
        HashSet<string> reported,
        List<TemplateFinding> findings)
    {
        if (settled.Contains(key))
        {
            return;
        }

        if (!walking.Add(key))
        {
            // The cycle is the tail of the path from where this key first appears. Reporting the whole loop rather
            // than one edge of it is what lets an administrator see which rule to cut.
            var from = path.IndexOf(key);
            var cycle = path[(from < 0 ? 0 : from)..];
            var signature = string.Join("→", cycle.Order(StringComparer.Ordinal));

            if (reported.Add(signature))
            {
                findings.Add(new TemplateFinding(
                    TemplateFindingSeverity.Error,
                    "cyclic-condition",
                    $"The visibility rules on {string.Join(" → ", [.. cycle, key])} form a loop, so none of them "
                    + "can be settled.",
                    $"fields[{cycle[0]}].rule"));
            }

            return;
        }

        path.Add(key);

        if (graph.TryGetValue(key, out var referenced))
        {
            foreach (var next in referenced)
            {
                Walk(next, graph, settled, walking, path, reported, findings);
            }
        }

        path.RemoveAt(path.Count - 1);
        walking.Remove(key);
        settled.Add(key);
    }
}
