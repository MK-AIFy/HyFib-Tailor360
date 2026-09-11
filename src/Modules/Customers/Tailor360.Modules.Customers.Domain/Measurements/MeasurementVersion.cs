using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>
/// What a customer measured, once, against one template version. Never edited.
/// </summary>
/// <remarks>
/// <para>
/// <strong>INV-MSR-01: a version is never edited.</strong> A correction is a new version carrying a reason and
/// pointing back at the one it replaces; the old one stays readable forever. That is not caution — a garment job
/// snapshots the version it was confirmed against (INV-JOB-01), so editing one would change what a job already in
/// production was cut to. The type therefore exposes no mutator at all, and the table underneath it is append-only
/// against the application role. Two guards for one rule, because either alone has been enough to fail before.
/// </para>
/// <para>
/// <strong>It is keyed by customer and template version, not by customer and template.</strong> A template
/// publishing version 4 does not invalidate a measurement taken against version 3: the measurement renders through
/// the version it was taken under, forever, which is what makes a two-year-old job card readable
/// (<c>docs/prd/measurement-templates.md</c> section 11).
/// </para>
/// <para>
/// Nothing here is a customer's name, telephone number or address. A measurement is <strong>sensitive personal
/// data</strong> under <c>docs/nfr/data-classification.md</c> and the customer is a foreign identifier and nothing
/// else — which is what lets a measurement sheet be printed and handed to a tailor.
/// </para>
/// </remarks>
public sealed class MeasurementVersion
{
    private readonly List<MeasurementValue> _values = [];

    private MeasurementVersion()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private MeasurementVersion(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        Guid templateId,
        Guid templateVersionId,
        int versionNumber,
        IEnumerable<MeasurementValue> values,
        DateTimeOffset takenAt,
        Guid? takenBy,
        string? reason,
        Guid? reusedFromVersionId,
        Guid? correctsVersionId)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CustomerId = customerId;
        TemplateId = templateId;
        TemplateVersionId = templateVersionId;
        VersionNumber = versionNumber;
        TakenAt = takenAt;
        TakenBy = takenBy;
        Reason = reason;
        ReusedFromVersionId = reusedFromVersionId;
        CorrectsVersionId = correctsVersionId;

        _values.AddRange(values);
    }

    /// <summary>Identity of this version. What a garment job pins.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation it belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The branch it was taken at.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The customer it is about.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The template it answers.</summary>
    public Guid TemplateId { get; private set; }

    /// <summary>The template version it was captured against, and renders through forever.</summary>
    public Guid TemplateVersionId { get; private set; }

    /// <summary>Version 1, 2, 3 for this customer and template — never reused.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>When it was taken, in UTC.</summary>
    public DateTimeOffset TakenAt { get; private set; }

    /// <summary>Who took it.</summary>
    public Guid? TakenBy { get; private set; }

    /// <summary>Why, where a reason was demanded. Required on a correction.</summary>
    public string? Reason { get; private set; }

    /// <summary>The version these values were pre-filled from, or null when measured fresh.</summary>
    public Guid? ReusedFromVersionId { get; private set; }

    /// <summary>The version this one corrects, or null when it is not a correction.</summary>
    /// <remarks>
    /// Distinct from <see cref="ReusedFromVersionId"/> on purpose. Reuse says "these numbers came from there";
    /// correction says "that one was wrong". A screen reading them as one thing would tell a tailor a routine
    /// reuse had superseded a measurement it did not.
    /// </remarks>
    public Guid? CorrectsVersionId { get; private set; }

    /// <summary>What was measured. Fixed at confirmation.</summary>
    public IReadOnlyList<MeasurementValue> Values => _values;

    /// <summary>
    /// Turns a checked set of values into the record of a measurement.
    /// </summary>
    /// <remarks>
    /// Deliberately not a validator: <see cref="MeasurementConfirmation"/> decides whether the values are
    /// admissible, and this records the answer. Splitting them is what lets the wizard ask "would this be
    /// accepted?" without half-creating anything.
    /// </remarks>
    /// <param name="id">Identity of the version.</param>
    /// <param name="draft">The draft being confirmed. It must already be consumed.</param>
    /// <param name="versionNumber">The next number for this customer and template.</param>
    /// <param name="values">The values, already checked against the template version.</param>
    /// <param name="reason">Why, where one was given.</param>
    /// <param name="correctsVersionId">The version this corrects, or null.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">Who took it.</param>
    /// <returns>The version.</returns>
    public static MeasurementVersion Of(
        Guid id,
        MeasurementDraft draft,
        int versionNumber,
        IReadOnlyCollection<MeasurementValue> values,
        string? reason,
        Guid? correctsVersionId,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(values);

        return new MeasurementVersion(
            id,
            draft.OrganisationId,
            draft.BranchId,
            draft.CustomerId,
            draft.TemplateId,
            draft.TemplateVersionId,
            versionNumber,
            values,
            now,
            by,
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            draft.ReusedFromVersionId,
            correctsVersionId);
    }

    /// <summary>The value recorded for one field, or null when the field was not answered.</summary>
    /// <param name="key">The field key.</param>
    /// <returns>The value, or null.</returns>
    public MeasurementValue? ValueOf(string key)
        => _values.FirstOrDefault(value => string.Equals(value.Key.Value, key, StringComparison.Ordinal));
}

/// <summary>
/// Whether a draft may become a version, and what is wrong if it may not.
/// </summary>
/// <remarks>
/// <para>
/// This is INV-MSR-04 in one place. It is a separate type from both the draft and the version because three
/// callers need it and only one of them writes anything: the confirmation, the wizard asking whether the step it
/// is on would be accepted, and the administration preview from #96 asking the same question about invented
/// values. A second copy of "what a valid measurement is" is how a wizard comes to accept what the server then
/// refuses.
/// </para>
/// <para>
/// <strong>Every finding, not the first.</strong> A tailor holding a tape wants to be told about all four fields
/// at once, not to press Confirm four times. The one thing that stops the whole check is a value for a field the
/// version does not have, because that means the draft and the version have diverged and nothing else concluded
/// from them would be trustworthy.
/// </para>
/// </remarks>
public static class MeasurementConfirmation
{
    /// <summary>Checks a draft's values against the template version it is pinned to.</summary>
    /// <param name="version">The template version.</param>
    /// <param name="values">What was measured.</param>
    /// <param name="selections">The garment's design selections, empty outside an order.</param>
    /// <returns>Everything wrong, in field order. Empty when the draft may be confirmed.</returns>
    public static IReadOnlyList<Error> Check(
        TemplateVersion version,
        IReadOnlyCollection<MeasurementValue> values,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? selections = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(values);

        var byKey = new Dictionary<string, MeasurementValue>(StringComparer.Ordinal);
        var findings = new List<Error>();

        foreach (var value in values)
        {
            if (!byKey.TryAdd(value.Key.Value, value))
            {
                findings.Add(MeasurementErrors.DuplicateValue(value.Key.Value));
            }
        }

        var known = version.Fields.Select(field => field.Key.Value).ToHashSet(StringComparer.Ordinal);
        var strays = byKey.Keys.Where(key => !known.Contains(key)).ToArray();

        if (strays.Length > 0)
        {
            // The draft and the version have diverged, so nothing else concluded here would be trustworthy —
            // including which fields are "required", since a rule may read a field that no longer exists.
            return [.. strays.Order(StringComparer.Ordinal).Select(MeasurementErrors.UnknownField)];
        }

        // The rule operands are the values as a rule reads them: a field key to the string it holds. A measured
        // field is not a rule operand — only a choice can be compared to a set of codes — so a numeric value
        // contributes nothing and an undecidable rule shows its field, which is what `Evaluate` already does.
        var operands = byKey
            .Where(entry => entry.Value.Choice is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value.Choice!, StringComparer.Ordinal);

        var chosen = selections ?? new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var field in version.Fields.OrderBy(field => field.DisplayOrder).ThenBy(
                     field => field.Key.Value, StringComparer.Ordinal))
        {
            var shown = field.Rule?.Evaluate(operands, chosen).IsShown ?? true;

            if (!byKey.TryGetValue(field.Key.Value, out var value))
            {
                // A hidden field is not asked for, so its absence is the right answer rather than a gap.
                if (shown && field.IsRequired)
                {
                    findings.Add(MeasurementErrors.MissingRequiredValue(field.Key.Value));
                }

                continue;
            }

            var checkedValue = value.Validate(field);

            if (checkedValue.IsFailure)
            {
                findings.Add(checkedValue.Error);
            }
        }

        return findings;
    }
}
