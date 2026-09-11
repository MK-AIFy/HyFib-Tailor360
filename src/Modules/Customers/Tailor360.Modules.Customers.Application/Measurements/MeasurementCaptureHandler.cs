using Microsoft.Extensions.Options;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Application.Options;
using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Modules.Customers.Contracts.Events;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Customers.Application.Measurements;

/// <summary>
/// Measuring a garment: starting a draft, saving a step, and confirming the record.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Everything that matters here happens at confirmation.</strong> A draft is deliberately permissive
/// (INV-MSR-04) because a half-measured garment is a normal state, so this handler is mostly a thin wrapper around
/// the domain until <see cref="ConfirmAsync"/>, where four things become true at once: the values are checked, the
/// consent is checked, the draft is consumed, and the version and its event commit together.
/// </para>
/// <para>
/// <strong>No measurement is ever logged, audited or put in a problem detail.</strong> A measurement is sensitive
/// personal data under <c>docs/nfr/data-classification.md</c>; the audit entry names the customer, the template
/// version and the count of fields, and nothing else. A field-level problem detail names the <em>field</em> that
/// was refused and never the value that was refused — "chest is out of bounds", never "1500 mm is out of bounds".
/// </para>
/// </remarks>
/// <param name="store">The capture store.</param>
/// <param name="templates">The module's template store, for the version a draft is pinned to.</param>
/// <param name="consent">The module's own consent contract, for INV-MSR-05.</param>
/// <param name="events">This module's outbox.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="options">The capture options, for how long a draft lives.</param>
public sealed class MeasurementCaptureHandler(
    IMeasurementCaptureStore store,
    IMeasurementTemplateStore templates,
    IConsentQuery consent,
    ICustomersEventPublisher events,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    IOptions<MeasurementCaptureOptions> options)
{
    /// <summary>Measuring began.</summary>
    public const string DraftStartedAction = "customers.measurement.draft.started";

    /// <summary>One step of a draft was saved.</summary>
    public const string SectionSavedAction = "customers.measurement.section.saved";

    /// <summary>Measurements became a confirmed, immutable version.</summary>
    public const string ConfirmedAction = "customers.measurement.confirmed";

    /// <summary>Somebody read a customer's measurements as a sheet. A sensitive read (INV-MSR-06).</summary>
    public const string SheetReadAction = "customers.measurement.sheet.read";

    /// <summary>
    /// Starts measuring, or hands back the measuring already under way.
    /// </summary>
    /// <remarks>
    /// Idempotent by design rather than only by key: a branch has at most one open draft per customer and
    /// template, so a second call reaches the first draft. Two counters starting at the same instant is settled by
    /// the partial unique index, and the loser re-reads and finds the winner's.
    /// </remarks>
    /// <param name="command">Who is being measured, against what.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public async Task<Result<CapturedDraft>> StartAsync(
        StartMeasurementDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await store.FindOpenDraftAsync(
            command.BranchId,
            command.CustomerId,
            command.MeasurementTemplateId,
            command.OrganisationId,
            cancellationToken);

        if (existing is not null)
        {
            return Result.Success(new CapturedDraft(existing, store.EntityTagOf(existing)));
        }

        var template = await templates.FindAsync(
            command.MeasurementTemplateId, command.OrganisationId, cancellationToken);

        if (template is null)
        {
            return Result.Failure<CapturedDraft>(MeasurementErrors.TemplateNotFound);
        }

        if (template.PublishedVersion is not { } version)
        {
            return Result.Failure<CapturedDraft>(MeasurementErrors.TemplateVersionNotPublished);
        }

        var started = MeasurementDraft.Start(
            ids.NewId(),
            command.OrganisationId,
            command.BranchId,
            command.CustomerId,
            version,
            command.ReuseFromVersionId,
            clock.UtcNow,
            options.Value.DraftLifetime,
            command.By);

        if (started.IsFailure)
        {
            return Result.Failure<CapturedDraft>(started.Error);
        }

        var draft = started.Value;

        if (command.ReuseFromVersionId is { } sourceId)
        {
            var reused = await ReuseAsync(draft, version, sourceId, command, cancellationToken);

            if (reused.IsFailure)
            {
                return Result.Failure<CapturedDraft>(reused.Error);
            }
        }

        store.Add(draft);

        var saved = await store.SaveAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<CapturedDraft>(saved.Error);
        }

        return Result.Success(new CapturedDraft(draft, store.EntityTagOf(draft)));
    }

    /// <summary>Reads a draft.</summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be read.</returns>
    public async Task<Result<CapturedDraft>> ReadAsync(
        Guid draftId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var draft = await store.FindDraftAsync(draftId, organisationId, cancellationToken);

        return draft is null
            ? Result.Failure<CapturedDraft>(MeasurementErrors.DraftNotFound)
            : Result.Success(new CapturedDraft(draft, store.EntityTagOf(draft)));
    }

    /// <summary>Saves one wizard step.</summary>
    /// <param name="command">The step and everything measured in it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason the step was refused.</returns>
    public async Task<Result<CapturedDraft>> SaveSectionAsync(
        SaveMeasurementSectionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var located = await LoadForChangeAsync(
            command.DraftId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (located.IsFailure)
        {
            return Result.Failure<CapturedDraft>(located.Error);
        }

        var (draft, version) = located.Value;
        var converted = Convert(command.Values);

        if (converted.IsFailure)
        {
            return Result.Failure<CapturedDraft>(converted.Error);
        }

        var saved = draft.SaveSection(
            version, command.GroupName, converted.Value, clock.UtcNow, command.By);

        if (saved.IsFailure)
        {
            return Result.Failure<CapturedDraft>(saved.Error);
        }

        var committed = await store.SaveAsync(cancellationToken);

        if (committed.IsFailure)
        {
            return Result.Failure<CapturedDraft>(committed.Error);
        }

        return Result.Success(new CapturedDraft(draft, store.EntityTagOf(draft)));
    }

    /// <summary>
    /// Turns a draft into the immutable record of a measurement.
    /// </summary>
    /// <remarks>
    /// The order is the argument. Consent is checked before anything is written, because a consent withdrawn while
    /// a garment was being measured must stop the record from being made rather than delete it afterwards. The
    /// values are checked next, so a refusal names every field at once. Only then is the draft consumed and the
    /// version written — with its event, in one transaction.
    /// </remarks>
    /// <param name="command">Which draft, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason it could not be confirmed.</returns>
    public async Task<Result<MeasurementVersion>> ConfirmAsync(
        ConfirmMeasurementsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var located = await LoadForChangeAsync(
            command.DraftId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (located.IsFailure)
        {
            return Result.Failure<MeasurementVersion>(located.Error);
        }

        var (draft, version) = located.Value;

        // INV-MSR-05, and first: nothing is written before the shop is allowed to keep it.
        var standing = await consent.GetAsync(
            draft.CustomerId, ConsentPurposeKeys.MeasurementStorage, cancellationToken);

        if (!standing.IsGranted)
        {
            return Result.Failure<MeasurementVersion>(MeasurementErrors.MeasurementConsentMissing);
        }

        // A correction says "that one was wrong", and a record of a change to confirmed evidence without a reason
        // is a record of nothing.
        if (command.CorrectsVersionId is not null && string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<MeasurementVersion>(MeasurementErrors.Required("reason"));
        }

        // The refusal is a single error carrying a code the client branches on; the findings themselves come
        // back through `CheckAsync`, which the wizard is already asking before it offers Confirm at all. Same
        // shape as the catalogue's publish refusal, so a client learns one pattern rather than two.
        if (MeasurementConfirmation.Check(version, draft.Values).Count > 0)
        {
            return Result.Failure<MeasurementVersion>(MeasurementErrors.ConfirmationValidationFailed);
        }

        var consumed = draft.Consume(clock.UtcNow);

        if (consumed.IsFailure)
        {
            return Result.Failure<MeasurementVersion>(consumed.Error);
        }

        var number = await store.NextVersionNumberAsync(
            draft.CustomerId, draft.TemplateId, cancellationToken);

        var confirmed = MeasurementVersion.Of(
            ids.NewId(),
            draft,
            number,
            [.. draft.Values],
            command.Reason,
            command.CorrectsVersionId,
            clock.UtcNow,
            command.By);

        store.Add(confirmed);

        // Staged before the save, so the version, its values, the consumed draft and the event commit together or
        // not at all — the transactional boundary invariants.md section 4.3 fixes.
        events.Publish(new MeasurementVersionConfirmed(
            ids.NewId(),
            clock.UtcNow,
            confirmed.Id,
            confirmed.OrganisationId,
            confirmed.BranchId,
            confirmed.CustomerId,
            confirmed.TemplateId,
            confirmed.TemplateVersionId,
            confirmed.VersionNumber,
            confirmed.CorrectsVersionId));

        var saved = await store.SaveConfirmationAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<MeasurementVersion>(saved.Error);
        }

        // Against the customer, not the measurement: this is the one act here that belongs on a customer's
        // timeline, and the trail the timeline reads is keyed by customer. The entry carries identifiers and a
        // count — never a value, never a field key. A snapshot of a measurement *is* the measurement.
        await CustomerAudit.RecordAsync(
            audit,
            ConfirmedAction,
            confirmed.CustomerId,
            $"Measurement {confirmed.VersionNumber} confirmed against "
            + $"template version {confirmed.TemplateVersionId}, with {confirmed.Values.Count} value(s)."
            + (confirmed.CorrectsVersionId is null
                ? string.Empty
                : " It corrects an earlier measurement, which stays readable."),
            command.Reason,
            before: null,
            after: null,
            cancellationToken);

        return Result.Success(confirmed);
    }

    /// <summary>
    /// Says what stands between a draft and a confirmed measurement, changing nothing.
    /// </summary>
    /// <remarks>
    /// The wizard asks this before it offers Confirm, so a person is told about all four wrong fields at once
    /// rather than pressing Confirm four times. It is the same check the confirmation runs, which is what stops a
    /// wizard accepting what the server then refuses.
    /// </remarks>
    /// <param name="draftId">The draft.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Everything wrong, in field order. Empty when the draft may be confirmed.</returns>
    public async Task<Result<IReadOnlyList<Error>>> CheckAsync(
        Guid draftId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var located = await LoadForChangeAsync(draftId, organisationId, expected: null, cancellationToken);

        return located.IsFailure
            ? Result.Failure<IReadOnlyList<Error>>(located.Error)
            : Result.Success(MeasurementConfirmation.Check(located.Value.Version, located.Value.Draft.Values));
    }

    /// <summary>Reads one confirmed measurement.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason it could not be read.</returns>
    public async Task<Result<MeasurementVersion>> ReadVersionAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var found = await store.FindVersionAsync(versionId, organisationId, cancellationToken);

        return found is null
            ? Result.Failure<MeasurementVersion>(MeasurementErrors.MeasurementNotFound)
            : Result.Success(found);
    }

    /// <summary>A customer's confirmed measurements, newest first.</summary>
    /// <remarks>
    /// Every one, not the latest: choosing which to reuse is a decision somebody makes from a list with dates on
    /// it, and offering only the newest would make "reuse" mean "reuse the last one".
    /// </remarks>
    /// <param name="customerId">The customer.</param>
    /// <param name="templateId">The template, or null for every template.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The measurements, newest first.</returns>
    public async Task<IReadOnlyList<MeasurementVersion>> ListAsync(
        Guid customerId,
        Guid? templateId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await store.ListVersionsAsync(customerId, templateId, organisationId, cancellationToken);

    /// <summary>Compares two of a customer's measurements, oldest first.</summary>
    /// <param name="beforeId">The older measurement.</param>
    /// <param name="afterId">The newer measurement.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every field either holds, or the reason they could not be compared.</returns>
    public async Task<Result<MeasurementComparisonResult>> CompareAsync(
        Guid beforeId,
        Guid afterId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var before = await store.FindVersionAsync(beforeId, organisationId, cancellationToken);
        var after = await store.FindVersionAsync(afterId, organisationId, cancellationToken);

        if (before is null || after is null)
        {
            return Result.Failure<MeasurementComparisonResult>(MeasurementErrors.MeasurementNotFound);
        }

        var differences = MeasurementComparison.Compare(before, after);

        return differences.IsFailure
            ? Result.Failure<MeasurementComparisonResult>(differences.Error)
            : Result.Success(new MeasurementComparisonResult(before, after, differences.Value));
    }

    /// <summary>
    /// Reads one measurement as a sheet, and records that somebody did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// INV-MSR-06: reading a sheet is a sensitive read and is audited <strong>explicitly</strong>, here in the
    /// handler, rather than left to the request log. The distinction matters because a page view says a route was
    /// called and this says a named person's measurements were looked at — which is the question asked after the
    /// fact, and the one a request log cannot answer once it has rolled over.
    /// </para>
    /// <para>
    /// The entry is written <em>after</em> the read succeeds. An entry for a read that was refused would record an
    /// access that never happened, and the trail is believed.
    /// </para>
    /// </remarks>
    /// <param name="measurementVersionId">The measurement.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The measurement, or the reason it could not be read.</returns>
    public async Task<Result<MeasurementVersion>> ReadSheetAsync(
        Guid measurementVersionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var found = await store.FindVersionAsync(measurementVersionId, organisationId, cancellationToken);

        if (found is null)
        {
            return Result.Failure<MeasurementVersion>(MeasurementErrors.MeasurementNotFound);
        }

        await CustomerAudit.RecordAsync(
            audit,
            SheetReadAction,
            found.CustomerId,
            $"Measurement {found.VersionNumber} was read as a sheet. It carries "
            + $"{found.Values.Count} value(s); none of them is recorded here.",
            reason: null,
            before: null,
            after: null,
            cancellationToken);

        return Result.Success(found);
    }

    /// <summary>Pre-fills a fresh draft from an earlier measurement of the same template.</summary>
    /// <remarks>
    /// Values whose field is no longer on the published version are dropped rather than carried: the draft is
    /// pinned to the version being measured against, and a value for a field that version does not have could
    /// never be confirmed.
    /// </remarks>
    private async Task<Result> ReuseAsync(
        MeasurementDraft draft,
        TemplateVersion version,
        Guid sourceId,
        StartMeasurementDraftCommand command,
        CancellationToken cancellationToken)
    {
        var source = await store.FindVersionAsync(sourceId, command.OrganisationId, cancellationToken);

        if (source is null)
        {
            return Result.Failure(MeasurementErrors.MeasurementNotFound);
        }

        if (source.CustomerId != command.CustomerId || source.TemplateId != command.MeasurementTemplateId)
        {
            // Reusing another customer's measurements, or another garment's, is the mistake this exists to
            // prevent — and it would be invisible on a screen showing only numbers.
            return Result.Failure(MeasurementErrors.ReuseSourceDoesNotMatch);
        }

        var known = version.Fields.Select(field => field.Key.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var group in version.Fields.Select(field => field.GroupName).Distinct(StringComparer.Ordinal))
        {
            var keysHere = version.Fields
                .Where(field => string.Equals(field.GroupName, group, StringComparison.Ordinal))
                .Select(field => field.Key.Value)
                .ToHashSet(StringComparer.Ordinal);

            var carried = source.Values
                .Where(value => known.Contains(value.Key.Value) && keysHere.Contains(value.Key.Value))
                .ToArray();

            if (carried.Length == 0)
            {
                continue;
            }

            var saved = draft.SaveSection(version, group, carried, clock.UtcNow, command.By);

            if (saved.IsFailure)
            {
                return saved;
            }
        }

        return Result.Success();
    }

    private static Result<IReadOnlyCollection<MeasurementValue>> Convert(IReadOnlyList<CapturedValue> values)
    {
        var converted = new List<MeasurementValue>(values.Count);

        foreach (var value in values)
        {
            var key = FieldKey.Create(value.Key);

            if (key.IsFailure)
            {
                return Result.Failure<IReadOnlyCollection<MeasurementValue>>(key.Error);
            }

            if (value.Choice is { Length: > 0 } code)
            {
                converted.Add(MeasurementValue.Chosen(key.Value, code));
                continue;
            }

            if (value.Entered is not { } entered)
            {
                return Result.Failure<IReadOnlyCollection<MeasurementValue>>(
                    MeasurementErrors.Required(value.Key));
            }

            // Converted here rather than trusted from the caller: a client that rounds differently from the
            // server would otherwise store a number the server would never have produced, and the difference
            // would only ever show up as a garment that does not fit.
            converted.Add(MeasurementValue.Measured(
                key.Value,
                UnitConversion.ToMillimetres(entered, value.Unit),
                value.Unit,
                value.Acknowledged));
        }

        return Result.Success<IReadOnlyCollection<MeasurementValue>>(converted);
    }

    private async Task<Result<(MeasurementDraft Draft, TemplateVersion Version)>> LoadForChangeAsync(
        Guid draftId,
        Guid organisationId,
        Tailor360.Platform.Abstractions.Concurrency.EntityTag? expected,
        CancellationToken cancellationToken)
    {
        var draft = await store.FindDraftAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<(MeasurementDraft, TemplateVersion)>(MeasurementErrors.DraftNotFound);
        }

        if (expected is { } tag && !tag.Matches(store.EntityTagOf(draft)))
        {
            return Result.Failure<(MeasurementDraft, TemplateVersion)>(MeasurementErrors.DraftChanged);
        }

        var template = await templates.FindByVersionAsync(
            draft.TemplateVersionId, organisationId, cancellationToken);

        var version = template?.Versions.SingleOrDefault(one => one.Id == draft.TemplateVersionId);

        return version is null
            ? Result.Failure<(MeasurementDraft, TemplateVersion)>(MeasurementErrors.VersionNotFound)
            : Result.Success((draft, version));
    }
}

/// <summary>Two measurements and what differs between them.</summary>
/// <param name="Before">The older measurement.</param>
/// <param name="After">The newer measurement.</param>
/// <param name="Differences">Every field either holds, in key order.</param>
public sealed record MeasurementComparisonResult(
    MeasurementVersion Before,
    MeasurementVersion After,
    IReadOnlyList<MeasurementDifference> Differences);
