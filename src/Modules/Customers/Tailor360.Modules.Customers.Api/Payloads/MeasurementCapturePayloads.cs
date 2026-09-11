using Tailor360.Modules.Customers.Application.Measurements;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Api.Payloads;

/// <summary>
/// What a caller sends for one field.
/// </summary>
/// <remarks>
/// The number is sent <em>as it was typed</em>, with the unit beside it, and the server converts. A client that
/// sent millimetres would be asserting a conversion, and a client that rounds differently from the server would
/// store a value the server would never have produced — a difference that only ever shows up as a garment that
/// does not fit.
/// </remarks>
/// <param name="Key">The field key, as the template version declares it.</param>
/// <param name="Entered">The number as typed, in <paramref name="Unit"/>. Null for a chosen field.</param>
/// <param name="Unit">The unit it was typed in: <c>Inch</c>, <c>Centimetre</c> or <c>Count</c>.</param>
/// <param name="Choice">The option code, for a chosen field. Null for a measured one.</param>
/// <param name="Acknowledged">Whether an unusual value was explicitly accepted by the person measuring.</param>
public sealed record MeasurementValueRequest(
    string Key,
    decimal? Entered,
    string? Unit,
    string? Choice,
    bool Acknowledged)
{
    /// <summary>Reads the unit, defaulting to inches, which is what a tape in this shop reads.</summary>
    /// <returns>The captured value as the application takes it.</returns>
    public CapturedValue ToCaptured()
        => new(
            Key,
            Entered,
            Enum.TryParse<DisplayUnit>(Unit, ignoreCase: true, out var unit) ? unit : DisplayUnit.Inch,
            Choice,
            Acknowledged);
}

/// <summary>Start measuring a garment, or pick up the measuring already under way.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="MeasurementTemplateId">The template to measure against.</param>
/// <param name="ReuseFromVersionId">An earlier measurement to pre-fill from, or null to measure fresh.</param>
public sealed record StartMeasurementDraftRequest(
    Guid CustomerId,
    Guid MeasurementTemplateId,
    Guid? ReuseFromVersionId);

/// <summary>Save one wizard step.</summary>
/// <param name="GroupName">The step being saved, as the template version names it.</param>
/// <param name="Values">Everything measured in that step. It replaces the step; it does not merge into it.</param>
public sealed record SaveMeasurementSectionRequest(
    string GroupName,
    IReadOnlyList<MeasurementValueRequest> Values);

/// <summary>Turn a draft into the record of a measurement.</summary>
/// <param name="Reason">Why. Required when this corrects an earlier measurement.</param>
/// <param name="CorrectsVersionId">The measurement this replaces, or null when it is a fresh one.</param>
public sealed record ConfirmMeasurementsRequest(string? Reason, Guid? CorrectsVersionId);

/// <summary>One recorded value, as a screen reads it.</summary>
/// <remarks>
/// <strong>Millimetres, and the unit it was taken in.</strong> The caller renders it — a tailor who took a chest
/// in inches and reads it back in centimetres will re-measure, and be right to. Rendering here would mean
/// rounding here, and a rounded value in a payload is a value nobody can convert back.
/// </remarks>
/// <param name="Key">The field key.</param>
/// <param name="Millimetres">The canonical value, or null for a chosen field.</param>
/// <param name="EnteredUnit">The unit it was taken in.</param>
/// <param name="Choice">The option code, or null for a measured field.</param>
/// <param name="Acknowledged">Whether an unusual value was explicitly accepted.</param>
public sealed record MeasurementValuePayload(
    string Key,
    decimal? Millimetres,
    string EnteredUnit,
    string? Choice,
    bool Acknowledged)
{
    /// <summary>Renders one value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The payload.</returns>
    public static MeasurementValuePayload From(MeasurementValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new MeasurementValuePayload(
            value.Key.Value,
            value.Millimetres,
            value.EnteredUnit.ToString(),
            value.Choice,
            value.Acknowledged);
    }
}

/// <summary>A garment being measured.</summary>
/// <param name="MeasurementDraftId">Identity of the draft.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="BranchId">The branch measuring. Drafts are shared within it.</param>
/// <param name="MeasurementTemplateId">The template.</param>
/// <param name="TemplateVersionId">The version it is pinned to, and will be confirmed against.</param>
/// <param name="ReusedFromVersionId">The measurement it was pre-filled from, or null.</param>
/// <param name="StartedAt">When measuring began, in UTC.</param>
/// <param name="UpdatedAt">When it was last written to, in UTC.</param>
/// <param name="ExpiresAt">When it stops being worth confirming, in UTC.</param>
/// <param name="ConsumedAt">When it became a measurement, or null while it is still work in progress.</param>
/// <param name="Values">What has been measured so far.</param>
public sealed record MeasurementDraftPayload(
    Guid MeasurementDraftId,
    Guid CustomerId,
    Guid BranchId,
    Guid MeasurementTemplateId,
    Guid TemplateVersionId,
    Guid? ReusedFromVersionId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    IReadOnlyList<MeasurementValuePayload> Values)
{
    /// <summary>Renders one draft.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>The payload.</returns>
    public static MeasurementDraftPayload From(MeasurementDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new MeasurementDraftPayload(
            draft.Id,
            draft.CustomerId,
            draft.BranchId,
            draft.TemplateId,
            draft.TemplateVersionId,
            draft.ReusedFromVersionId,
            draft.StartedAt,
            draft.UpdatedAt,
            draft.ExpiresAt,
            draft.ConsumedAt,
            [.. draft.Values.Select(MeasurementValuePayload.From)]);
    }
}

/// <summary>A confirmed measurement. Never edited.</summary>
/// <param name="MeasurementVersionId">Identity of the measurement. What a garment job pins.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="BranchId">The branch it was taken at.</param>
/// <param name="MeasurementTemplateId">The template it answers.</param>
/// <param name="TemplateVersionId">The version it was captured against, and renders through forever.</param>
/// <param name="VersionNumber">Which measurement this is for that customer and template.</param>
/// <param name="TakenAt">When it was taken, in UTC.</param>
/// <param name="TakenBy">Who took it.</param>
/// <param name="Reason">Why, where one was given.</param>
/// <param name="ReusedFromVersionId">The measurement its values were pre-filled from, or null.</param>
/// <param name="CorrectsVersionId">The measurement it replaces, or null.</param>
/// <param name="Values">What was measured.</param>
public sealed record MeasurementVersionPayload(
    Guid MeasurementVersionId,
    Guid CustomerId,
    Guid BranchId,
    Guid MeasurementTemplateId,
    Guid TemplateVersionId,
    int VersionNumber,
    DateTimeOffset TakenAt,
    Guid? TakenBy,
    string? Reason,
    Guid? ReusedFromVersionId,
    Guid? CorrectsVersionId,
    IReadOnlyList<MeasurementValuePayload> Values)
{
    /// <summary>Renders one confirmed measurement.</summary>
    /// <param name="version">The measurement.</param>
    /// <returns>The payload.</returns>
    public static MeasurementVersionPayload From(MeasurementVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new MeasurementVersionPayload(
            version.Id,
            version.CustomerId,
            version.BranchId,
            version.TemplateId,
            version.TemplateVersionId,
            version.VersionNumber,
            version.TakenAt,
            version.TakenBy,
            version.Reason,
            version.ReusedFromVersionId,
            version.CorrectsVersionId,
            [.. version.Values.Select(MeasurementValuePayload.From)]);
    }
}

/// <summary>One thing standing between a draft and a confirmed measurement.</summary>
/// <remarks>
/// It names the <strong>field</strong> and never the value. "Chest is outside what this field can hold" is
/// actionable; "1500 mm is outside what this field can hold" would put a measurement in a problem detail, which
/// is logged, relayed and read by people the measurement is not for.
/// </remarks>
/// <param name="Code">The stable dotted code a screen branches on.</param>
/// <param name="Field">The field key it is about, or null when it is about the draft as a whole.</param>
/// <param name="Message">What is wrong, in the shop's words.</param>
public sealed record MeasurementFindingPayload(string Code, string? Field, string Message)
{
    /// <summary>Renders one finding.</summary>
    /// <param name="error">The finding.</param>
    /// <returns>The payload.</returns>
    public static MeasurementFindingPayload From(Error error) => new(error.Code, error.Target, error.Message);
}

/// <summary>What a draft would be refused for if it were confirmed now.</summary>
/// <param name="MeasurementDraftId">The draft that was checked.</param>
/// <param name="Confirmable">Whether it may be confirmed as it stands.</param>
/// <param name="Findings">Everything wrong, in field order. Empty when it may be confirmed.</param>
public sealed record MeasurementCheckPayload(
    Guid MeasurementDraftId,
    bool Confirmable,
    IReadOnlyList<MeasurementFindingPayload> Findings)
{
    /// <summary>Renders the answer of a check.</summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="findings">What the check found.</param>
    /// <returns>The payload.</returns>
    public static MeasurementCheckPayload From(Guid draftId, IReadOnlyList<Error> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return new MeasurementCheckPayload(
            draftId, findings.Count == 0, [.. findings.Select(MeasurementFindingPayload.From)]);
    }
}
