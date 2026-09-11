using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Snapshots;

/// <summary>
/// The measurement copy frozen onto a garment job at confirmation.
/// </summary>
/// <remarks>
/// <para>
/// INV-JOB-01: the snapshot is <strong>immutable after confirmation</strong>, and there is no mutator
/// on this type at all. Capturing a new measurement for the customer, republishing the template or
/// correcting a field never changes what a confirmed job says — the tailor is making the garment that
/// was agreed, not the one the record has since become.
/// </para>
/// <para>
/// <strong><see cref="MeasurementVersionId"/> is provenance and nothing else.</strong> It is nullable
/// because it is a reference rather than a restricting foreign key
/// (<c>docs/IMPLEMENTATION_PLAN.md</c> issue #32a): a customer merge or a retention sweep may take the
/// row it points at away, and a job card that stops rendering because a pointer went stale would be
/// exactly the coupling the copy was made to avoid. Nothing reads it for a value.
/// </para>
/// <para>
/// The template version is carried for the same reason: a sheet renders through the version it was
/// taken under, with that version's labels, order and diagrams, for ever.
/// </para>
/// </remarks>
public sealed record MeasurementSnapshot
{
    /// <summary>
    /// The only way to build one, and it is private so that <see cref="Create"/> is the only way in.
    /// </summary>
    /// <remarks>
    /// Not a positional record, and the properties are get-only rather than <c>init</c>, so <c>with</c>
    /// cannot bypass the factory. On a type whose whole purpose is that it never changes, a second way
    /// in would be a way to change it.
    /// </remarks>
    private MeasurementSnapshot(
        Guid? measurementVersionId,
        Guid measurementTemplateId,
        Guid templateVersionId,
        int versionNumber,
        DateTimeOffset takenAt,
        Guid? takenBy,
        IReadOnlyList<MeasuredValue> values,
        DateTimeOffset frozenAt)
    {
        MeasurementVersionId = measurementVersionId;
        MeasurementTemplateId = measurementTemplateId;
        TemplateVersionId = templateVersionId;
        VersionNumber = versionNumber;
        TakenAt = takenAt;
        TakenBy = takenBy;
        Values = values;
        FrozenAt = frozenAt;
    }

    /// <summary>
    /// Provenance. The measurement version these values were copied from, or null where the row is gone.
    /// </summary>
    public Guid? MeasurementVersionId { get; }

    /// <summary>The template the measurement answers.</summary>
    public Guid MeasurementTemplateId { get; }

    /// <summary>The template version it renders through, for ever.</summary>
    public Guid TemplateVersionId { get; }

    /// <summary>Which measurement this was for that customer and template, at the moment it was copied.</summary>
    public int VersionNumber { get; }

    /// <summary>When the measurement was taken, in UTC. Copied, and not the moment of the snapshot.</summary>
    public DateTimeOffset TakenAt { get; }

    /// <summary>Who took it, where a person did.</summary>
    public Guid? TakenBy { get; }

    /// <summary>The values, as they stood at confirmation.</summary>
    public IReadOnlyList<MeasuredValue> Values { get; }

    /// <summary>When the copy was frozen onto the job, in UTC.</summary>
    public DateTimeOffset FrozenAt { get; }

    /// <summary>
    /// Takes the copy.
    /// </summary>
    /// <remarks>
    /// An empty value list is accepted. A template that carries no field is a catalogue question for
    /// whoever published it, and refusing the confirmation would stop a customer's order at the counter
    /// over a configuration mistake nobody at the counter can fix.
    /// </remarks>
    /// <param name="measurementVersionId">The measurement version copied from, as provenance only.</param>
    /// <param name="measurementTemplateId">The template the measurement answers.</param>
    /// <param name="templateVersionId">The template version it renders through.</param>
    /// <param name="versionNumber">Which measurement this was for that customer and template.</param>
    /// <param name="takenAt">When the measurement was taken, in UTC.</param>
    /// <param name="takenBy">Who took it.</param>
    /// <param name="values">The values to copy.</param>
    /// <param name="frozenAt">The instant of the confirmation, from <c>IClock</c>.</param>
    /// <returns>The snapshot, or the first failure found.</returns>
    public static Result<MeasurementSnapshot> Create(
        Guid? measurementVersionId,
        Guid measurementTemplateId,
        Guid templateVersionId,
        int versionNumber,
        DateTimeOffset takenAt,
        Guid? takenBy,
        IReadOnlyCollection<MeasuredValue> values,
        DateTimeOffset frozenAt)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (measurementTemplateId == Guid.Empty)
        {
            return Result.Failure<MeasurementSnapshot>(OrdersErrors.Required("measurementTemplateId"));
        }

        if (templateVersionId == Guid.Empty)
        {
            return Result.Failure<MeasurementSnapshot>(OrdersErrors.Required("templateVersionId"));
        }

        // Measurement versions are numbered from one, so a zero is the default of an int nobody filled
        // in rather than a real version, and a job card headed "version 0" helps nobody.
        if (versionNumber < 1)
        {
            return Result.Failure<MeasurementSnapshot>(OrdersErrors.Required("versionNumber"));
        }

        var keys = new HashSet<string>(values.Count, StringComparer.Ordinal);

        foreach (var value in values)
        {
            // A hole in the list is a caller defect, but it is a caller defect this factory answers with
            // a refusal rather than with a NullReferenceException thrown from inside it: a Create that
            // returns a Result must return one for everything it was handed (convention [5]).
            if (value is null)
            {
                return Result.Failure<MeasurementSnapshot>(OrdersErrors.Required("values"));
            }

            // Two rows answering one field is a copy nobody can read back: which of the two is the
            // measurement the garment was cut to?
            if (!keys.Add(value.Key))
            {
                return Result.Failure<MeasurementSnapshot>(
                    OrdersErrors.DuplicateMeasurementKey(value.Key));
            }
        }

        return Result.Success(new MeasurementSnapshot(
            measurementVersionId,
            measurementTemplateId,
            templateVersionId,
            versionNumber,
            takenAt,
            takenBy,
            [.. values],
            frozenAt));
    }
}
