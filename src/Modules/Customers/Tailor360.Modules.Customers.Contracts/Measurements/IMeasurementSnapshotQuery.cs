namespace Tailor360.Modules.Customers.Contracts.Measurements;

/// <summary>
/// One confirmed measurement, as another module reads it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It answers about a measurement by its own identity, never about "the latest".</strong> That is
/// INV-JOB-01 expressed as a contract shape: a garment job snapshots the measurement it was confirmed against and
/// does not move when a newer one appears, so a method called <c>GetLatestFor(customer, template)</c> would make
/// the wrong thing easy and the right thing an extra step. Orders pins an identifier at confirmation and reads it
/// back with this, forever.
/// </para>
/// <para>
/// <strong>What comes back is measurements and nothing else about the person.</strong> No name, no telephone
/// number, no address — a measurement is sensitive personal data under
/// <c>docs/nfr/data-classification.md</c> and the customer is an identifier here. That is also what lets the
/// result be rendered onto a sheet and handed to a tailor.
/// </para>
/// <para>
/// The values are in millimetres with the unit each was taken in. The caller renders them, because rendering
/// means rounding and a rounded value in a contract is a value nobody can convert back — a tailor who took a
/// chest in inches and reads it in centimetres will re-measure, and be right to.
/// </para>
/// </remarks>
public interface IMeasurementSnapshotQuery
{
    /// <summary>One confirmed measurement by its own identity.</summary>
    /// <param name="measurementVersionId">The measurement a job pinned.</param>
    /// <param name="organisationId">The organisation it must belong to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The measurement, or null when none of that identity exists.</returns>
    Task<MeasurementSnapshot?> GetAsync(
        Guid measurementVersionId,
        Guid organisationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What each of these measurements is about — whose it is and which template it answers — without its
    /// values, for a caller checking a set of pins at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pin is a claim about a customer and a template as much as about a version: a garment reusing "the
    /// customer's blouse measurement" is wrong the moment the version named belongs to somebody else, or answers
    /// a template the service does not measure by. Existence alone cannot tell a caller either, so this answers
    /// the header and not only the identifier — and never the values, which are sensitive personal data a pin
    /// has no business holding (<c>docs/nfr/data-classification.md</c>). A caller that needs the values asks
    /// <see cref="GetAsync"/>, one measurement at a time, and is audited for it.
    /// </para>
    /// <para>
    /// Batched because an order confirming several garments pins one measurement per garment, and one round trip
    /// per garment would make a large order slow to confirm for no reason. An identifier that names nothing in
    /// the organisation is simply absent from the answer.
    /// </para>
    /// </remarks>
    /// <param name="measurementVersionIds">The measurements to ask about.</param>
    /// <param name="organisationId">The organisation they must belong to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A header per measurement that exists, in no particular order.</returns>
    Task<IReadOnlyList<MeasurementVersionHeader>> DescribeAsync(
        IReadOnlyCollection<Guid> measurementVersionIds,
        Guid organisationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One confirmed measurement without its values: what a pin can be checked against.
/// </summary>
/// <remarks>
/// The first seven members of <see cref="MeasurementSnapshot"/>, and deliberately not the eighth: a header
/// says whose measurement this is and what it answers, which is everything a caller holding an identifier
/// needs to know whether the identifier is the right one, and nothing a tailor would need a consent to read.
/// </remarks>
/// <param name="MeasurementVersionId">Its own identity. What a garment pins.</param>
/// <param name="CustomerId">The customer it is about. An identifier and nothing else.</param>
/// <param name="BranchId">The branch it was taken at.</param>
/// <param name="MeasurementTemplateId">The template it answers.</param>
/// <param name="TemplateVersionId">The template version it renders through, forever.</param>
/// <param name="VersionNumber">Which measurement this is for that customer and template.</param>
/// <param name="TakenAt">When it was taken, in UTC.</param>
public sealed record MeasurementVersionHeader(
    Guid MeasurementVersionId,
    Guid CustomerId,
    Guid BranchId,
    Guid MeasurementTemplateId,
    Guid TemplateVersionId,
    int VersionNumber,
    DateTimeOffset TakenAt);

/// <summary>One confirmed measurement, as another module reads it.</summary>
/// <param name="MeasurementVersionId">Its own identity. What a garment job pins.</param>
/// <param name="CustomerId">The customer it is about. An identifier and nothing else.</param>
/// <param name="BranchId">The branch it was taken at.</param>
/// <param name="MeasurementTemplateId">The template it answers.</param>
/// <param name="TemplateVersionId">The template version it renders through, forever.</param>
/// <param name="VersionNumber">Which measurement this is for that customer and template.</param>
/// <param name="TakenAt">When it was taken, in UTC.</param>
/// <param name="TakenBy">Who took it.</param>
/// <param name="CorrectsVersionId">The measurement it replaces, or null.</param>
/// <param name="Values">What was measured.</param>
public sealed record MeasurementSnapshot(
    Guid MeasurementVersionId,
    Guid CustomerId,
    Guid BranchId,
    Guid MeasurementTemplateId,
    Guid TemplateVersionId,
    int VersionNumber,
    DateTimeOffset TakenAt,
    Guid? TakenBy,
    Guid? CorrectsVersionId,
    IReadOnlyList<MeasuredValueSnapshot> Values);

/// <summary>One recorded value.</summary>
/// <remarks>
/// Millimetres and the unit it was taken in, never a rendered string. A caller showing it to a tailor renders it
/// in the unit it was taken in; a caller comparing two of them compares the millimetres.
/// </remarks>
/// <param name="Key">The field key, which is what values are filed under across template versions.</param>
/// <param name="Millimetres">The canonical value, or null for a field that is chosen rather than measured.</param>
/// <param name="EnteredUnit">The unit it was entered in: <c>Inch</c>, <c>Centimetre</c> or <c>Count</c>.</param>
/// <param name="Choice">The option code, or null for a measured field.</param>
/// <param name="Acknowledged">Whether an unusual value was explicitly accepted by the person measuring.</param>
public sealed record MeasuredValueSnapshot(
    string Key,
    decimal? Millimetres,
    string EnteredUnit,
    string? Choice,
    bool Acknowledged);
