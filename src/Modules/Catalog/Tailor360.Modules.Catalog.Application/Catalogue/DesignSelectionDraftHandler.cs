using Microsoft.Extensions.Options;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Application.Options;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Modules.Catalog.Domain.Design;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// What the picker reads, what a draft does, and what a republish leaves for it to catch up on (#30,
/// issue #140).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The picker reads the currently published version; a draft is pinned to whichever version it
/// started against.</strong> The two are the same version on an ordinary day and different ones exactly
/// when a republish happened mid-draft, which is the case <see cref="ReadAsync"/> and
/// <see cref="MigrateAsync"/> exist to answer for.
/// </para>
/// <para>
/// <strong>Nothing here decides whether a selection is allowed.</strong> <see cref="Save"/> on the
/// aggregate refuses only a value for a group or option this version does not have; everything else —
/// branch and date offerability, the requires and excludes rules, a required group left unset — is
/// <see cref="IDesignSelectionValidator"/>'s answer, asked by <see cref="CheckAsync"/> and again inside
/// <see cref="MigrateAsync"/>, never rebuilt here.
/// </para>
/// </remarks>
/// <param name="store">The catalogue store, for the version a draft is pinned to and the currently published one.</param>
/// <param name="draftStore">The draft store.</param>
/// <param name="availability">What a branch may order, for the offerability gate the picker and the start route apply.</param>
/// <param name="validator">The rule evaluator.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="options">How long a draft lives.</param>
public sealed class DesignSelectionDraftHandler(
    ICatalogStore store,
    IDesignSelectionDraftStore draftStore,
    ICatalogAvailabilityQuery availability,
    IDesignSelectionValidator validator,
    IClock clock,
    IIdGenerator ids,
    IOptions<DesignSelectionDraftOptions> options)
{
    /// <summary>A design selection draft was started.</summary>
    public const string DraftStartedAction = "catalog.design_selection_draft.started";

    /// <summary>A draft's whole selection set was saved.</summary>
    public const string DraftSavedAction = "catalog.design_selection_draft.saved";

    /// <summary>A draft was re-pinned to a later catalogue version.</summary>
    public const string DraftMigratedAction = "catalog.design_selection_draft.migrated";

    /// <summary>What the picker shows for one service type at the caller's branch, today.</summary>
    /// <param name="serviceTypeId">The service type, from the currently published version.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchId">The caller's branch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The groups, options and rules offerable here today, or the reason there are none.</returns>
    public async Task<Result<DesignPickerRead>> ReadPickerAsync(
        Guid serviceTypeId,
        Guid organisationId,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var version = await store.FindPublishedAsync(organisationId, cancellationToken);

        if (version is null)
        {
            return Result.Failure<DesignPickerRead>(CatalogErrors.NoPublishedVersion);
        }

        var service = version.FindService(serviceTypeId);

        if (service is null)
        {
            return Result.Failure<DesignPickerRead>(CatalogErrors.ServiceTypeNotOrderableHere);
        }

        if (!await availability.IsOrderableAsync(serviceTypeId, branchId, clock.UtcNow, cancellationToken))
        {
            return Result.Failure<DesignPickerRead>(CatalogErrors.ServiceTypeNotOrderableHere);
        }

        var today = TodayAt(clock.UtcNow);

        var offeredGroups = service.DesignOptionGroupIds
            .Select(version.FindDesignGroup)
            .Where(group => group is not null && group.IsActiveOn(today) && group.BranchIds.Contains(branchId))
            .Select(group => group!)
            .ToList();

        var offeredCodes = offeredGroups.Select(group => group.Code).ToHashSet(StringComparer.Ordinal);

        var rules = version.DesignRulesOf(service.CategoryId)
            .Where(rule => rule.Details.GroupCodes.All(offeredCodes.Contains))
            .ToList();

        return Result.Success(new DesignPickerRead(version.Id, service.CategoryId, service.Id, offeredGroups, rules));
    }

    /// <summary>Starts choosing a design, or refuses when nothing is published or offered here.</summary>
    /// <param name="command">Which service type, where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public async Task<Result<CapturedDesignDraft>> StartAsync(
        StartDesignSelectionDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var version = await store.FindPublishedAsync(command.OrganisationId, cancellationToken);

        if (version is null)
        {
            return Result.Failure<CapturedDesignDraft>(CatalogErrors.NoPublishedVersion);
        }

        var service = version.FindService(command.ServiceTypeId);

        if (service is null)
        {
            return Result.Failure<CapturedDesignDraft>(CatalogErrors.ServiceTypeNotOrderableHere);
        }

        if (!await availability.IsOrderableAsync(
                command.ServiceTypeId, command.BranchId, clock.UtcNow, cancellationToken))
        {
            return Result.Failure<CapturedDesignDraft>(CatalogErrors.ServiceTypeNotOrderableHere);
        }

        var started = DesignSelectionDraft.Start(
            ids.NewId(),
            command.OrganisationId,
            command.BranchId,
            version,
            service,
            clock.UtcNow,
            options.Value.DraftLifetime,
            command.By);

        if (started.IsFailure)
        {
            return Result.Failure<CapturedDesignDraft>(started.Error);
        }

        draftStore.Add(started.Value);

        var saved = await draftStore.SaveAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<CapturedDesignDraft>(saved.Error);
        }

        return Result.Success(new CapturedDesignDraft(started.Value, draftStore.EntityTagOf(started.Value)));
    }

    /// <summary>Reads a draft, with the migration prompt a republish may have left standing.</summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft and its prompt, or the reason it could not be read.</returns>
    public async Task<Result<DesignDraftRead>> ReadAsync(
        Guid draftId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var draft = await draftStore.FindAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<DesignDraftRead>(CatalogErrors.DesignDraftNotFound);
        }

        var plan = await PlanMigrationAsync(draft, organisationId, cancellationToken);

        // A prompt is shown whenever the pin is behind the current publish, whether or not anything the
        // plan found touches this garment's own groups — the pin holding is itself worth knowing, and a
        // client that only checked HasChanges would stay silent about a service type that vanished
        // outright, which carries no per-group change of its own.
        return Result.Success(new DesignDraftRead(
            draft, draftStore.EntityTagOf(draft), plan is { UpToDate: false } ? plan : null));
    }

    /// <summary>Replaces the whole selection set of a draft.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason the save was refused.</returns>
    public async Task<Result<CapturedDesignDraft>> SaveAsync(
        SaveDesignSelectionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadForChangeAsync(
            command.DraftId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CapturedDesignDraft>(loaded.Error);
        }

        var draft = loaded.Value;

        var pinned = await LoadPinnedAsync(draft, command.OrganisationId, cancellationToken);

        if (pinned.IsFailure)
        {
            return Result.Failure<CapturedDesignDraft>(pinned.Error);
        }

        var (version, service) = pinned.Value;

        var saved = draft.Save(
            version, service.CategoryId, command.Selections, command.Instructions, clock.UtcNow, command.By);

        if (saved.IsFailure)
        {
            return Result.Failure<CapturedDesignDraft>(saved.Error);
        }

        var committed = await draftStore.SaveAsync(cancellationToken);

        if (committed.IsFailure)
        {
            return Result.Failure<CapturedDesignDraft>(committed.Error);
        }

        return Result.Success(new CapturedDesignDraft(draft, draftStore.EntityTagOf(draft)));
    }

    /// <summary>Says what stands between a draft and confirmation, changing nothing.</summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="hasReferenceImage">Whether the garment holds a reference image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The evaluation, or the reason it could not be made.</returns>
    public async Task<Result<DesignEvaluation>> CheckAsync(
        Guid draftId,
        Guid organisationId,
        bool hasReferenceImage,
        CancellationToken cancellationToken = default)
    {
        var draft = await draftStore.FindAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<DesignEvaluation>(CatalogErrors.DesignDraftNotFound);
        }

        var pinned = await LoadPinnedAsync(draft, organisationId, cancellationToken);

        if (pinned.IsFailure)
        {
            return Result.Failure<DesignEvaluation>(pinned.Error);
        }

        var (version, service) = pinned.Value;

        return await validator.ValidateAsync(
            RequestOf(organisationId, version.Id, service.CategoryId, draft, hasReferenceImage, clock.UtcNow),
            cancellationToken);
    }

    /// <summary>Re-pins a draft to the currently published version and re-validates it.</summary>
    /// <remarks>
    /// Applies exactly the plan a preceding <see cref="ReadAsync"/> would have shown: the pin, the
    /// service type and the selections all move together in one save, and a selection whose option the
    /// republish retired is simply absent afterwards. Already-current is answered as a no-op success
    /// rather than a refusal, so a client that migrates defensively before every check never has to
    /// special-case "there was nothing to do".
    /// </remarks>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The migrated draft and its fresh evaluation, or the reason it could not be migrated.</returns>
    public async Task<Result<DesignMigrationOutcome>> MigrateAsync(
        MigrateDesignSelectionDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadForChangeAsync(
            command.DraftId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<DesignMigrationOutcome>(loaded.Error);
        }

        var draft = loaded.Value;

        var currentVersion = await store.FindPublishedAsync(command.OrganisationId, cancellationToken);

        if (currentVersion is null)
        {
            return Result.Failure<DesignMigrationOutcome>(CatalogErrors.NoPublishedVersion);
        }

        var plan = await PlanMigrationAsync(draft, command.OrganisationId, cancellationToken, currentVersion);

        if (plan is null)
        {
            return Result.Failure<DesignMigrationOutcome>(CatalogErrors.VersionNotFound);
        }

        if (!plan.ServiceTypeStillOffered)
        {
            return Result.Failure<DesignMigrationOutcome>(CatalogErrors.ServiceTypeNoLongerOffered);
        }

        if (!plan.UpToDate)
        {
            var migrated = draft.MigrateTo(
                plan.TargetCatalogVersionId, plan.TargetServiceTypeId!.Value, plan.MigratedSelections,
                clock.UtcNow, command.By);

            if (migrated.IsFailure)
            {
                return Result.Failure<DesignMigrationOutcome>(migrated.Error);
            }

            var committed = await draftStore.SaveAsync(cancellationToken);

            if (committed.IsFailure)
            {
                return Result.Failure<DesignMigrationOutcome>(committed.Error);
            }
        }

        var service = currentVersion.FindService(draft.ServiceTypeId);

        if (service is null)
        {
            return Result.Failure<DesignMigrationOutcome>(CatalogErrors.ServiceTypeNotFound);
        }

        var evaluated = await validator.ValidateAsync(
            RequestOf(
                command.OrganisationId, currentVersion.Id, service.CategoryId, draft,
                command.HasReferenceImage, clock.UtcNow),
            cancellationToken);

        if (evaluated.IsFailure)
        {
            return Result.Failure<DesignMigrationOutcome>(evaluated.Error);
        }

        return Result.Success(new DesignMigrationOutcome(
            draft, draftStore.EntityTagOf(draft), plan.Changes, evaluated.Value));
    }

    /// <summary>
    /// Marks a draft spent. A capability, not a route: no caller exists until #32a's order confirmation
    /// reaches for it, through whichever of the boundary's five mechanisms that issue settles on.
    /// </summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the reason it could not be consumed.</returns>
    public async Task<Result> ConsumeAsync(
        Guid draftId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var draft = await draftStore.FindAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure(CatalogErrors.DesignDraftNotFound);
        }

        var consumed = draft.Consume(clock.UtcNow);

        return consumed.IsFailure ? consumed : await draftStore.SaveAsync(cancellationToken);
    }

    private async Task<DesignSelectionMigrationPlan?> PlanMigrationAsync(
        DesignSelectionDraft draft,
        Guid organisationId,
        CancellationToken cancellationToken,
        CatalogVersion? currentVersion = null)
    {
        var pinnedVersion = await store.FindAsync(draft.CatalogVersionId, organisationId, cancellationToken);

        if (pinnedVersion is null)
        {
            return null;
        }

        currentVersion ??= await store.FindPublishedAsync(organisationId, cancellationToken);

        if (currentVersion is null)
        {
            return null;
        }

        return DesignSelectionMigration.Plan(pinnedVersion, draft.ServiceTypeId, currentVersion, draft.Selections);
    }

    private async Task<Result<(CatalogVersion Version, ServiceType Service)>> LoadPinnedAsync(
        DesignSelectionDraft draft,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var version = await store.FindAsync(draft.CatalogVersionId, organisationId, cancellationToken);

        if (version is null)
        {
            return Result.Failure<(CatalogVersion, ServiceType)>(CatalogErrors.VersionNotFound);
        }

        var service = version.FindService(draft.ServiceTypeId);

        return service is null
            ? Result.Failure<(CatalogVersion, ServiceType)>(CatalogErrors.ServiceTypeNotFound)
            : Result.Success((version, service));
    }

    private async Task<Result<DesignSelectionDraft>> LoadForChangeAsync(
        Guid draftId,
        Guid organisationId,
        EntityTag expected,
        CancellationToken cancellationToken)
    {
        var draft = await draftStore.FindAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<DesignSelectionDraft>(CatalogErrors.DesignDraftNotFound);
        }

        // The token belongs to the whole selection set. Compared before any domain mutation, exactly as
        // CatalogHandler and MeasurementCaptureHandler both do.
        return expected.Matches(draftStore.EntityTagOf(draft))
            ? Result.Success(draft)
            : Result.Failure<DesignSelectionDraft>(CatalogErrors.DesignDraftChanged);
    }

    private static DesignSelectionRequest RequestOf(
        Guid organisationId,
        Guid catalogVersionId,
        Guid categoryId,
        DesignSelectionDraft draft,
        bool hasReferenceImage,
        DateTimeOffset now)
        => new(
            organisationId,
            catalogVersionId,
            categoryId,
            draft.BranchId,
            TodayAt(now),
            [.. draft.Selections.Select(selection => new DesignSelection(selection.GroupCode, selection.OptionCodes))],
            hasReferenceImage);

    /// <summary>
    /// The instant read as a date in the branch's timezone.
    /// </summary>
    /// <remarks>
    /// India Standard Time, matching <c>CatalogAvailabilityQuery.TodayAt</c> and the same accepted gap:
    /// per-branch timezones arrive with #25's branch record, and this is where they will be read from
    /// once they do.
    /// </remarks>
    private static DateOnly TodayAt(DateTimeOffset at)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, IndiaTimeZone.Instance).DateTime);
}
