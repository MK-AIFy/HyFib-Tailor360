using System.Text.Json;
using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// How a field's rule and its choice options are written into their JSON columns.
/// </summary>
/// <remarks>
/// <para>
/// Two parts of a template are documents rather than rows: the visibility rule and the option list. Both are only
/// ever read and written whole, both are small, and giving each its own table would cost a join per field on the
/// path that renders the capture wizard. The plan's blueprint for #27 says the rule language is JSON for the same
/// reason.
/// </para>
/// <para>
/// The options are camel-cased and written without indentation because the column is data, not a document
/// somebody reads. Nothing here is culture-sensitive: the rule holds codes and the options hold codes and labels,
/// and no number is formatted.
/// </para>
/// </remarks>
public static class MeasurementJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Writes a rule, or null when the field always shows.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The JSON, or null.</returns>
    public static string? Write(ConditionalRule? rule)
        => rule is null ? null : JsonSerializer.Serialize(rule, Options);

    /// <summary>Reads a rule back.</summary>
    /// <param name="json">The JSON, or null.</param>
    /// <returns>The rule, or null.</returns>
    public static ConditionalRule? ReadRule(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ConditionalRule>(json, Options);

    /// <summary>Writes a field's choice options.</summary>
    /// <param name="options">The options, possibly empty.</param>
    /// <returns>The JSON.</returns>
    public static string Write(IReadOnlyList<ChoiceOption> options)
        => JsonSerializer.Serialize(options, Options);

    /// <summary>Reads a field's choice options back.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The options, empty when there are none.</returns>
    public static IReadOnlyList<ChoiceOption> ReadOptions(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<ChoiceOption>>(json, Options) ?? [];
}
