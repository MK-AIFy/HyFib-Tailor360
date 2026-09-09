using System.Security.Cryptography;
using System.Text;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Identifiers;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// Synthetic fixtures for the measurement-template tests.
/// </summary>
/// <remarks>
/// Identifiers are derived from a name rather than generated, so a failing assertion names the thing it is about
/// and a test that depends on ordering does not change its mind between runs.
/// </remarks>
internal static class MeasurementTestData
{
    /// <summary>A fixed instant, so that nothing here reads the clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The organisation every fixture belongs to.</summary>
    public static readonly Guid OrganisationId = Id("organisation");

    /// <summary>A stable identifier for a name.</summary>
    /// <param name="name">What the identifier stands for.</param>
    /// <returns>The identifier.</returns>
    public static Guid Id(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes(name))[..16]);

    /// <summary>A template with no versions.</summary>
    /// <param name="code">The template code.</param>
    /// <returns>The template.</returns>
    public static MeasurementTemplate Template(string code = "MT_BLOUSE_PATTERN")
        => MeasurementTemplate.Create(
            Id(code), OrganisationId, code, "Blouse, Pattern", "Seeded for the tests.", Now, null).Value;

    /// <summary>A template carrying one draft version.</summary>
    /// <param name="code">The template code.</param>
    /// <returns>The template and its draft.</returns>
    public static (MeasurementTemplate Template, TemplateVersion Draft) WithDraft(
        string code = "MT_BLOUSE_PATTERN")
    {
        var template = Template(code);
        var draft = template.StartDraft(
            new CountingIds(), "Version 1", null, DisplayUnit.Inch, null, Now, null).Value;

        return (template, draft);
    }

    /// <summary>A numeric field definition with sensible defaults.</summary>
    /// <param name="key">The field key.</param>
    /// <param name="required">Whether it must be answered.</param>
    /// <param name="rule">Its visibility rule, or null.</param>
    /// <param name="bands">Its bands, or null for a wide default.</param>
    /// <returns>The definition.</returns>
    public static TemplateFieldDefinition Field(
        string key,
        bool required = true,
        ConditionalRule? rule = null,
        ValidationBands? bands = null)
        => new(
            key,
            key.Replace('_', ' '),
            null,
            "Bodice",
            0,
            CanonicalUnit.Millimetre,
            FieldPrecision.Eighths,
            bands ?? new ValidationBands(100m, 2000m, 200m, 1800m),
            required,
            "Body measurement. Round the fullest part, tape level.",
            "blouse_front_v1",
            null,
            "From one point to the other, tape level.",
            rule,
            []);

    /// <summary>A choice field definition.</summary>
    /// <param name="key">The field key.</param>
    /// <param name="codes">The option codes.</param>
    /// <returns>The definition.</returns>
    public static TemplateFieldDefinition Choice(string key, params string[] codes)
        => Field(key) with
        {
            CanonicalUnit = CanonicalUnit.None,
            Precision = FieldPrecision.Whole,
            Bands = ValidationBands.None,
            Options = [.. codes.Select((code, index) => new ChoiceOption(code, code, null, index))],
        };

    /// <summary>Identifiers that count up, so a cloned tree is inspectable.</summary>
    /// <remarks>
    /// The prefix matters. Two generators started with the same one hand out the same identifiers, which in a test
    /// that builds two versions produces two rows claiming one identity — a collision the real UUIDv7 generator
    /// cannot have, and therefore a fixture bug rather than a finding.
    /// </remarks>
    /// <param name="prefix">What makes this generator's identifiers distinct from another's.</param>
    internal sealed class CountingIds(string prefix = "generated") : IIdGenerator
    {
        private int _next;

        /// <inheritdoc />
        public Guid NewId() => Id($"{prefix}-{_next++}");
    }
}
