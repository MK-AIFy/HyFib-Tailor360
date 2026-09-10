using Microsoft.Extensions.Logging;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Events;
using Tailor360.Modules.Catalog.Domain;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Catalog.Application.Catalogue;

/// <summary>
/// Everything the catalogue does: drafting a version, editing its tree, publishing it and retiring it.
/// </summary>
/// <remarks>
/// <para>
/// The handler owns the sequence of steps, the validators and the audit entry; the aggregate owns the
/// invariants and the store owns the queries. It takes the caller's organisation as a parameter rather
/// than reading a security context, so every path through it can be exercised without a host.
/// </para>
/// <para>
/// <strong>Publishing a version retires the one it replaces, in the same transaction.</strong> Section
/// 7 of <c>docs/prd/category-hierarchy.md</c> says exactly one published version is current at any
/// time, and the same section refuses a retirement that would strand work in progress unless a
/// successor carries a replacement for every service type that work uses. Those two sentences only fit
/// together one way: publication is the supersession, and the retirement validators are asked about
/// the outgoing version with the incoming one named as its successor. So a draft that quietly dropped
/// a category three garments are mid-production against is refused at publish, which is where somebody
/// can still do something about it — rather than at a retirement nobody would think to attempt.
/// </para>
/// <para>
/// <strong>A validator that fails is not a validator that found nothing.</strong> Every registered
/// validator is asked even after an earlier one reported errors, because one report naming everything
/// wrong is worth far more than five rounds of fixing one reference at a time. But a validator that
/// <em>throws</em> stops the command outright: "we could not check" is a different answer from "we
/// checked and it is right", and only one of them is a basis for publishing.
/// </para>
/// </remarks>
/// <param name="store">The catalogue store.</param>
/// <param name="validators">Every registered validator, this module's built-in one included.</param>
/// <param name="events">This module's outbox publisher.</param>
/// <param name="cache">This node's cached versions, cleared when one of them stops being true.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="logger">The logger, for a validator that fell over.</param>
public sealed class CatalogHandler(
    ICatalogStore store,
    IEnumerable<ICatalogDependencyValidator> validators,
    ICatalogEventPublisher events,
    ICatalogCache cache,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    ILogger<CatalogHandler> logger)
{
    /// <summary>A draft catalogue version was started.</summary>
    public const string DraftedAction = "catalog.version.drafted";

    /// <summary>A catalogue version was cloned into a new draft.</summary>
    public const string ClonedAction = "catalog.version.cloned";

    /// <summary>A category was added to a draft.</summary>
    public const string CategoryAddedAction = "catalog.category.added";

    /// <summary>A category was changed in a draft.</summary>
    public const string CategoryChangedAction = "catalog.category.changed";

    /// <summary>A category was removed from a draft.</summary>
    public const string CategoryRemovedAction = "catalog.category.removed";

    /// <summary>A service type was added to a draft.</summary>
    public const string ServiceTypeAddedAction = "catalog.service_type.added";

    /// <summary>A service type was changed in a draft.</summary>
    public const string ServiceTypeChangedAction = "catalog.service_type.changed";

    /// <summary>A service type was removed from a draft.</summary>
    public const string ServiceTypeRemovedAction = "catalog.service_type.removed";

    /// <summary>A catalogue version became the active configuration.</summary>
    public const string PublishedAction = "catalog.version.published";

    /// <summary>A catalogue version stopped being offered.</summary>
    public const string RetiredAction = "catalog.version.retired";

    /// <summary>A label, description or display order was corrected on a published version.</summary>
    public const string LabelCorrectedAction = "catalog.label_corrected";

    private static readonly Action<ILogger, string, Guid, Exception> ValidatorFailed =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Error,
            new EventId(2901, nameof(ValidatorFailed)),
            "Catalogue validator {Validator} failed while checking version {VersionId}.");

    /// <summary>Starts a draft, empty or cloned from an existing version.</summary>
    /// <param name="command">What to start and from where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public async Task<Result<AdministeredCatalogVersion>> CreateDraftAsync(
        CreateCatalogDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var number = await store.NextVersionNumberAsync(command.OrganisationId, cancellationToken);
        var now = clock.UtcNow;

        Result<CatalogVersion> created;
        string action;
        string summary;

        if (command.CloneFromVersionId is { } sourceId)
        {
            var source = await store.FindAsync(sourceId, command.OrganisationId, cancellationToken);

            if (source is null)
            {
                return Result.Failure<AdministeredCatalogVersion>(CatalogErrors.VersionNotFound);
            }

            created = source.CloneAsDraft(ids, number, command.Name, command.Notes, now, command.By);
            action = ClonedAction;
            summary = $"Catalogue version {number} started as a copy of version {source.VersionNumber}.";
        }
        else
        {
            created = CatalogVersion.CreateDraft(
                ids.NewId(), command.OrganisationId, number, command.Name, command.Notes, now, command.By);
            action = DraftedAction;
            summary = $"Catalogue version {number} started empty.";
        }

        if (created.IsFailure)
        {
            return Result.Failure<AdministeredCatalogVersion>(created.Error);
        }

        var version = created.Value;

        store.Add(version);

        var saved = await store.SaveDraftAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredCatalogVersion>(saved.Error);
        }

        await CatalogAudit.RecordAsync(
            audit, action, version.Id, summary, null, null,
            CatalogVersionSnapshot.Of(version), cancellationToken);

        return Result.Success(Administered(version));
    }

    /// <summary>Reads one version and everything in it.</summary>
    /// <param name="versionId">The version.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason it could not be read.</returns>
    public async Task<Result<AdministeredCatalogVersion>> ReadAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var version = await store.FindAsync(versionId, organisationId, cancellationToken);

        return version is null
            ? Result.Failure<AdministeredCatalogVersion>(CatalogErrors.VersionNotFound)
            : Result.Success(Administered(version));
    }

    /// <summary>Reads the published version and everything in it.</summary>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published version, or the reason there is none.</returns>
    public async Task<Result<AdministeredCatalogVersion>> ReadPublishedAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var version = await store.FindPublishedAsync(organisationId, cancellationToken);

        return version is null
            ? Result.Failure<AdministeredCatalogVersion>(CatalogErrors.NoPublishedVersion)
            : Result.Success(Administered(version));
    }

    /// <summary>Lists the organisation's versions, newest first.</summary>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The versions.</returns>
    public async Task<IReadOnlyList<CatalogVersion>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
        => await store.ListAsync(organisationId, cancellationToken);

    /// <summary>Adds a category to a draft.</summary>
    /// <param name="command">Which draft, under which parent, and what to say about it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The category, or the reason it could not be added.</returns>
    public async Task<Result<Category>> AddCategoryAsync(
        AddCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<Category>(found.Error);
        }

        var version = found.Value;
        var added = version.AddCategory(
            ids.NewId(), ids.NewId(), command.ParentCategoryId, command.Details, clock.UtcNow, command.By);

        if (added.IsFailure)
        {
            return added;
        }

        await store.SaveAsync(cancellationToken);

        await CatalogAudit.RecordAsync(
            audit,
            CategoryAddedAction,
            version.Id,
            $"Category '{added.Value.Code}' added to catalogue version {version.VersionNumber}.",
            null,
            null,
            CatalogEntrySnapshot.Of(added.Value, ParentCodeOf(version, added.Value)),
            cancellationToken);

        return added;
    }

    /// <summary>Replaces what a draft says about a category.</summary>
    /// <param name="command">Which category, and what it should now say.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The category, or the reason it could not be changed.</returns>
    public async Task<Result<Category>> EditCategoryAsync(
        EditCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<Category>(found.Error);
        }

        var version = found.Value;
        var before = version.Find(command.CategoryId) is { } existing
            ? CatalogEntrySnapshot.Of(existing, ParentCodeOf(version, existing))
            : null;

        var edited = version.EditCategory(
            command.CategoryId, command.ParentCategoryId, command.Details, clock.UtcNow, command.By);

        if (edited.IsFailure)
        {
            return edited;
        }

        await store.SaveAsync(cancellationToken);

        await CatalogAudit.RecordAsync(
            audit,
            CategoryChangedAction,
            version.Id,
            $"Category '{edited.Value.Code}' changed in catalogue version {version.VersionNumber}.",
            command.Reason,
            before,
            CatalogEntrySnapshot.Of(edited.Value, ParentCodeOf(version, edited.Value)),
            cancellationToken);

        return edited;
    }

    /// <summary>Removes a category, its descendants and their service types from a draft.</summary>
    /// <param name="command">Which category.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the reason it could not be removed.</returns>
    public async Task<Result> RemoveCategoryAsync(
        RemoveCategoryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        var version = found.Value;
        var before = version.Find(command.CategoryId) is { } existing
            ? CatalogEntrySnapshot.Of(existing, ParentCodeOf(version, existing))
            : null;

        var descendants = version.DescendantsOf(command.CategoryId).Count;
        var removed = version.RemoveCategory(command.CategoryId, clock.UtcNow, command.By);

        if (removed.IsFailure)
        {
            return removed;
        }

        await store.SaveAsync(cancellationToken);

        await CatalogAudit.RecordAsync(
            audit,
            CategoryRemovedAction,
            version.Id,
            $"Category '{before?.Code}' removed from catalogue version {version.VersionNumber}, with "
            + $"{descendants} sub-categor{(descendants == 1 ? "y" : "ies")} beneath it.",
            command.Reason,
            before,
            null,
            cancellationToken);

        return removed;
    }

    /// <summary>Adds a service type to a category in a draft.</summary>
    /// <param name="command">Which category, and what to say about the service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The service type, or the reason it could not be added.</returns>
    public async Task<Result<ServiceType>> AddServiceTypeAsync(
        AddServiceTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ServiceType>(found.Error);
        }

        var version = found.Value;
        var added = version.AddServiceType(
            ids.NewId(), ids.NewId(), command.CategoryId, command.Details, clock.UtcNow, command.By);

        if (added.IsFailure)
        {
            return added;
        }

        await store.SaveAsync(cancellationToken);

        var categoryCode = version.Find(command.CategoryId)?.Code ?? string.Empty;

        await CatalogAudit.RecordAsync(
            audit,
            ServiceTypeAddedAction,
            version.Id,
            $"Service type '{categoryCode}.{added.Value.Code}' added to catalogue version "
            + $"{version.VersionNumber}.",
            null,
            null,
            CatalogEntrySnapshot.Of(added.Value, categoryCode),
            cancellationToken);

        return added;
    }

    /// <summary>Replaces what a draft says about a service type.</summary>
    /// <param name="command">Which service type, and what it should now say.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The service type, or the reason it could not be changed.</returns>
    public async Task<Result<ServiceType>> EditServiceTypeAsync(
        EditServiceTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<ServiceType>(found.Error);
        }

        var version = found.Value;
        var existing = version.FindService(command.ServiceTypeId);
        var categoryCode = existing is null
            ? string.Empty
            : version.Find(existing.CategoryId)?.Code ?? string.Empty;
        var before = existing is null ? null : CatalogEntrySnapshot.Of(existing, categoryCode);

        var edited = version.EditServiceType(
            command.ServiceTypeId, command.Details, clock.UtcNow, command.By);

        if (edited.IsFailure)
        {
            return edited;
        }

        await store.SaveAsync(cancellationToken);

        await CatalogAudit.RecordAsync(
            audit,
            ServiceTypeChangedAction,
            version.Id,
            $"Service type '{categoryCode}.{edited.Value.Code}' changed in catalogue version "
            + $"{version.VersionNumber}.",
            command.Reason,
            before,
            CatalogEntrySnapshot.Of(edited.Value, categoryCode),
            cancellationToken);

        return edited;
    }

    /// <summary>Removes a service type from a draft.</summary>
    /// <param name="command">Which service type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the reason it could not be removed.</returns>
    public async Task<Result> RemoveServiceTypeAsync(
        RemoveServiceTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        var version = found.Value;
        var existing = version.FindService(command.ServiceTypeId);
        var categoryCode = existing is null
            ? string.Empty
            : version.Find(existing.CategoryId)?.Code ?? string.Empty;
        var before = existing is null ? null : CatalogEntrySnapshot.Of(existing, categoryCode);

        var removed = version.RemoveServiceType(command.ServiceTypeId, clock.UtcNow, command.By);

        if (removed.IsFailure)
        {
            return removed;
        }

        await store.SaveAsync(cancellationToken);

        await CatalogAudit.RecordAsync(
            audit,
            ServiceTypeRemovedAction,
            version.Id,
            $"Service type '{categoryCode}.{before?.Code}' removed from catalogue version "
            + $"{version.VersionNumber}.",
            command.Reason,
            before,
            null,
            cancellationToken);

        return removed;
    }

    /// <summary>
    /// Runs every validator against a draft and reports what they found, changing nothing.
    /// </summary>
    /// <remarks>
    /// The preview an administrator sees before publishing. It is the same code path publication takes,
    /// deliberately: a preview that ran different checks from the command it previews would be worse
    /// than no preview.
    /// </remarks>
    /// <param name="versionId">The draft.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report, or the reason it could not be produced.</returns>
    public async Task<Result<CatalogValidationReport>> ValidateAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var found = await LoadAsync(versionId, organisationId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<CatalogValidationReport>(found.Error);
        }

        return await CheckPublicationAsync(found.Value, cancellationToken);
    }

    /// <summary>Publishes a draft, superseding whatever was published before it.</summary>
    /// <param name="command">Which draft, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was published and what it replaced, or the reason it could not be.</returns>
    public async Task<Result<CatalogPublication>> PublishAsync(
        PublishCatalogVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<CatalogPublication>(found.Error);
        }

        var draft = found.Value;

        var checkedDraft = await CheckPublicationAsync(draft, cancellationToken);

        if (checkedDraft.IsFailure)
        {
            return Result.Failure<CatalogPublication>(checkedDraft.Error);
        }

        var report = checkedDraft.Value;

        if (report.HasErrors)
        {
            return Result.Failure<CatalogPublication>(CatalogErrors.PublishValidationFailed);
        }

        var outgoing = await store.FindPublishedAsync(command.OrganisationId, cancellationToken);

        if (outgoing is not null)
        {
            var supersession = await CheckRetirementAsync(outgoing, draft, cancellationToken);

            if (supersession.IsFailure)
            {
                return Result.Failure<CatalogPublication>(supersession.Error);
            }

            if (supersession.Value.HasErrors)
            {
                return Result.Failure<CatalogPublication>(CatalogErrors.PublishValidationFailed);
            }

            var superseded = outgoing.Retire(
                clock.UtcNow,
                command.By,
                $"Superseded by catalogue version {draft.VersionNumber}: {command.Reason}");

            if (superseded.IsFailure)
            {
                return Result.Failure<CatalogPublication>(superseded.Error);
            }
        }

        var published = draft.Publish(clock.UtcNow, command.By, command.Reason);

        if (published.IsFailure)
        {
            return Result.Failure<CatalogPublication>(published.Error);
        }

        events.Publish(new CatalogVersionPublished(
            ids.NewId(),
            clock.UtcNow,
            draft.Id,
            draft.OrganisationId,
            draft.VersionNumber,
            outgoing?.Id));

        var saved = await store.SavePublicationAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<CatalogPublication>(saved.Error);
        }

        // The published version has changed, so what this node has cached about the outgoing one is
        // no longer what any read should start from. Its own next read is what this protects; another
        // replica reaches the same place by asking which version is published, which is never cached.
        cache.Invalidate();

        var notOrderable = draft.ServiceTypes.Count(service => service.NotOrderable);

        await CatalogAudit.RecordAsync(
            audit,
            PublishedAction,
            draft.Id,
            $"Catalogue version {draft.VersionNumber} published with {draft.Categories.Count} "
            + $"categor{(draft.Categories.Count == 1 ? "y" : "ies")} and {draft.ServiceTypes.Count} "
            + $"service type(s), {notOrderable} of them not orderable"
            + (outgoing is null
                ? ", as the first published version."
                : $", superseding version {outgoing.VersionNumber}."),
            command.Reason,
            outgoing is null ? null : CatalogVersionSnapshot.Of(outgoing),
            CatalogVersionSnapshot.Of(draft),
            cancellationToken);

        return Result.Success(new CatalogPublication(
            Administered(draft), outgoing?.Id, report.Findings));
    }

    /// <summary>Retires the published version, so that nothing new is taken against it.</summary>
    /// <param name="command">Which version, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The retired version, or the reason it could not be retired.</returns>
    public async Task<Result<AdministeredCatalogVersion>> RetireAsync(
        RetireCatalogVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<AdministeredCatalogVersion>(found.Error);
        }

        var version = found.Value;
        var before = CatalogVersionSnapshot.Of(version);

        var checkedRetirement = await CheckRetirementAsync(version, null, cancellationToken);

        if (checkedRetirement.IsFailure)
        {
            return Result.Failure<AdministeredCatalogVersion>(checkedRetirement.Error);
        }

        if (checkedRetirement.Value.HasErrors)
        {
            return Result.Failure<AdministeredCatalogVersion>(
                CatalogErrors.RetirementWouldBreakOrders);
        }

        var retired = version.Retire(clock.UtcNow, command.By, command.Reason);

        if (retired.IsFailure)
        {
            return Result.Failure<AdministeredCatalogVersion>(retired.Error);
        }

        events.Publish(new CatalogVersionRetired(
            ids.NewId(), clock.UtcNow, version.Id, version.OrganisationId, version.VersionNumber));

        await store.SaveAsync(cancellationToken);

        cache.Invalidate();

        await CatalogAudit.RecordAsync(
            audit,
            RetiredAction,
            version.Id,
            $"Catalogue version {version.VersionNumber} retired. Nothing new is taken against it; work "
            + "already in production runs to dispatch on the configuration it was pinned to.",
            command.Reason,
            before,
            CatalogVersionSnapshot.Of(version),
            cancellationToken);

        return Result.Success(Administered(version));
    }

    /// <summary>Corrects a label, Tamil label, description or display order on a published version.</summary>
    /// <param name="command">What to correct, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The corrected version, or the reason it could not be corrected.</returns>
    public async Task<Result<AdministeredCatalogVersion>> CorrectPresentationAsync(
        CorrectCatalogPresentationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(
            command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<AdministeredCatalogVersion>(found.Error);
        }

        var version = found.Value;
        ICatalogAuditState? before;
        ICatalogAuditState after;
        string what;

        if (command.CategoryId is { } categoryId)
        {
            before = version.Find(categoryId) is { } existing
                ? CatalogEntrySnapshot.Of(existing, ParentCodeOf(version, existing))
                : null;

            var corrected = version.CorrectCategoryPresentation(
                categoryId, command.Presentation, command.Reason, clock.UtcNow, command.By);

            if (corrected.IsFailure)
            {
                return Result.Failure<AdministeredCatalogVersion>(corrected.Error);
            }

            after = CatalogEntrySnapshot.Of(corrected.Value, ParentCodeOf(version, corrected.Value));
            what = $"category '{corrected.Value.Code}'";
        }
        else if (command.ServiceTypeId is { } serviceTypeId)
        {
            var existing = version.FindService(serviceTypeId);
            var categoryCode = existing is null
                ? string.Empty
                : version.Find(existing.CategoryId)?.Code ?? string.Empty;

            before = existing is null ? null : CatalogEntrySnapshot.Of(existing, categoryCode);

            var corrected = version.CorrectServiceTypePresentation(
                serviceTypeId, command.Presentation, command.Reason, clock.UtcNow, command.By);

            if (corrected.IsFailure)
            {
                return Result.Failure<AdministeredCatalogVersion>(corrected.Error);
            }

            after = CatalogEntrySnapshot.Of(corrected.Value, categoryCode);
            what = $"service type '{categoryCode}.{corrected.Value.Code}'";
        }
        else
        {
            return Result.Failure<AdministeredCatalogVersion>(
                CatalogErrors.Required("categoryId or serviceTypeId"));
        }

        await store.SaveAsync(cancellationToken);

        // The one mutation a published version admits, and therefore the one that can make a cached
        // entry wrong rather than merely unreachable.
        cache.Invalidate();

        await CatalogAudit.RecordAsync(
            audit,
            LabelCorrectedAction,
            version.Id,
            $"Presentation corrected on {what} in catalogue version {version.VersionNumber}. Labels are "
            + "read by people and by nothing else, so nothing priced, worked to or reported changes.",
            command.Reason,
            before,
            after,
            cancellationToken);

        return Result.Success(Administered(version));
    }

    private async Task<Result<CatalogValidationReport>> CheckPublicationAsync(
        CatalogVersion version,
        CancellationToken cancellationToken)
    {
        var ledger = await store.ReadCodeHistoryAsync(version.OrganisationId, cancellationToken);
        var candidate = CatalogProjection.ToCandidate(version, ledger);
        var findings = new List<AttributedFinding>();

        foreach (var validator in validators)
        {
            try
            {
                var found = await validator.ValidatePublicationAsync(candidate, cancellationToken);

                findings.AddRange(found.Select(finding => new AttributedFinding(validator.Name, finding)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // A validator is another module's code; one that fell over must stop
            catch (Exception exception) // the command rather than take the whole request down.
#pragma warning restore CA1031
            {
                ValidatorFailed(logger, validator.Name, version.Id, exception);

                return Result.Failure<CatalogValidationReport>(
                    CatalogErrors.ValidatorUnavailable(validator.Name));
            }
        }

        return Result.Success(new CatalogValidationReport(version.Id, findings));
    }

    private async Task<Result<CatalogValidationReport>> CheckRetirementAsync(
        CatalogVersion version,
        CatalogVersion? successor,
        CancellationToken cancellationToken)
    {
        var candidate = new CatalogRetirementCandidate(
            version.Id,
            version.OrganisationId,
            successor?.Id,
            successor is null
                ? new HashSet<Guid>()
                : successor.ServiceTypes.Select(service => service.Key).ToHashSet());

        var findings = new List<AttributedFinding>();

        foreach (var validator in validators)
        {
            try
            {
                var found = await validator.ValidateRetirementAsync(candidate, cancellationToken);

                findings.AddRange(found.Select(finding => new AttributedFinding(validator.Name, finding)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // As above: another module's code failing is not this command's answer.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                ValidatorFailed(logger, validator.Name, version.Id, exception);

                return Result.Failure<CatalogValidationReport>(
                    CatalogErrors.ValidatorUnavailable(validator.Name));
            }
        }

        return Result.Success(new CatalogValidationReport(version.Id, findings));
    }

    private async Task<Result<CatalogVersion>> LoadForChangeAsync(
        Guid versionId,
        Guid organisationId,
        EntityTag expectedVersion,
        CancellationToken cancellationToken)
    {
        var found = await LoadAsync(versionId, organisationId, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        // The token belongs to the whole version, including additions and removals in its tree.
        // Compare before any domain mutation, event publication or audit. The tracked xmin then
        // guards the remaining interval between this read and SaveChanges.
        return expectedVersion.Matches(store.EntityTagOf(found.Value))
            ? found
            : Result.Failure<CatalogVersion>(CatalogErrors.VersionChanged);
    }

    private async Task<Result<CatalogVersion>> LoadAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var version = await store.FindAsync(versionId, organisationId, cancellationToken);

        return version is null
            ? Result.Failure<CatalogVersion>(CatalogErrors.VersionNotFound)
            : Result.Success(version);
    }

    private AdministeredCatalogVersion Administered(CatalogVersion version)
        => new(version, store.EntityTagOf(version));

    private static string? ParentCodeOf(CatalogVersion version, Category category)
        => category.ParentId is { } parentId ? version.Find(parentId)?.Code : null;
}

/// <summary>A catalogue version and the token an edit to it must be made against.</summary>
/// <param name="Version">The version.</param>
/// <param name="Tag">Its <c>xmin</c>, as the <c>ETag</c> the client sends back as <c>If-Match</c>.</param>
public sealed record AdministeredCatalogVersion(CatalogVersion Version, EntityTag Tag);

/// <summary>One finding, and which validator made it.</summary>
/// <param name="Validator">The validator's name.</param>
/// <param name="Finding">What it found.</param>
public sealed record AttributedFinding(string Validator, CatalogFinding Finding);

/// <summary>What every validator said about one version.</summary>
/// <param name="VersionId">The version that was checked.</param>
/// <param name="Findings">Everything found, errors and warnings alike, in the order they were made.</param>
public sealed record CatalogValidationReport(Guid VersionId, IReadOnlyList<AttributedFinding> Findings)
{
    /// <summary>Whether anything found stops the command.</summary>
    public bool HasErrors
        => Findings.Any(found => found.Finding.Severity == CatalogFindingSeverity.Error);
}

/// <summary>What a publication did.</summary>
/// <param name="Published">The version that is now current.</param>
/// <param name="SupersededVersionId">The version it replaced, or null when it is the first.</param>
/// <param name="Findings">
/// What the validators said. Empty of errors by construction — publication would have been refused —
/// but the warnings are worth returning, because "published, and three services are not orderable" is
/// the sentence an administrator needs to read.
/// </param>
public sealed record CatalogPublication(
    AdministeredCatalogVersion Published,
    Guid? SupersededVersionId,
    IReadOnlyList<AttributedFinding> Findings);

/// <summary>Starts a draft catalogue version.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="Name">What this version is for.</param>
/// <param name="Notes">Longer notes for the reviewer.</param>
/// <param name="CloneFromVersionId">The version to copy, or null to start empty.</param>
/// <param name="By">The administrator.</param>
public sealed record CreateCatalogDraftCommand(
    Guid OrganisationId,
    string Name,
    string? Notes,
    Guid? CloneFromVersionId,
    Guid? By);

/// <summary>Adds a category to a draft.</summary>
/// <param name="VersionId">The draft.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token of the version the administrator reviewed.</param>
/// <param name="ParentCategoryId">The parent category, or null for top level.</param>
/// <param name="Details">What the administrator says about it.</param>
/// <param name="By">The administrator.</param>
public sealed record AddCategoryCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? ParentCategoryId,
    CategoryDetails Details,
    Guid? By);

/// <summary>Replaces what a draft says about a category.</summary>
/// <param name="VersionId">The draft.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token of the version the administrator reviewed.</param>
/// <param name="CategoryId">The category.</param>
/// <param name="ParentCategoryId">The parent it should have, or null for top level.</param>
/// <param name="Details">What it should now say.</param>
/// <param name="Reason">Why, where the administrator gave one.</param>
/// <param name="By">The administrator.</param>
public sealed record EditCategoryCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid CategoryId,
    Guid? ParentCategoryId,
    CategoryDetails Details,
    string? Reason,
    Guid? By);

/// <summary>Removes a category and everything beneath it from a draft.</summary>
/// <param name="VersionId">The draft.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token of the version the administrator reviewed.</param>
/// <param name="CategoryId">The category.</param>
/// <param name="Reason">Why, where the administrator gave one.</param>
/// <param name="By">The administrator.</param>
public sealed record RemoveCategoryCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid CategoryId,
    string? Reason,
    Guid? By);

/// <summary>Adds a service type to a category in a draft.</summary>
/// <param name="VersionId">The draft.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token of the version the administrator reviewed.</param>
/// <param name="CategoryId">The category that offers it.</param>
/// <param name="Details">What the administrator says about it.</param>
/// <param name="By">The administrator.</param>
public sealed record AddServiceTypeCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid CategoryId,
    ServiceTypeDetails Details,
    Guid? By);

/// <summary>Replaces what a draft says about a service type.</summary>
/// <param name="VersionId">The draft.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token of the version the administrator reviewed.</param>
/// <param name="ServiceTypeId">The service type.</param>
/// <param name="Details">What it should now say.</param>
/// <param name="Reason">Why, where the administrator gave one.</param>
/// <param name="By">The administrator.</param>
public sealed record EditServiceTypeCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid ServiceTypeId,
    ServiceTypeDetails Details,
    string? Reason,
    Guid? By);

/// <summary>Removes a service type from a draft.</summary>
/// <param name="VersionId">The draft.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token of the version the administrator reviewed.</param>
/// <param name="ServiceTypeId">The service type.</param>
/// <param name="Reason">Why, where the administrator gave one.</param>
/// <param name="By">The administrator.</param>
public sealed record RemoveServiceTypeCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid ServiceTypeId,
    string? Reason,
    Guid? By);

/// <summary>Publishes a draft.</summary>
/// <param name="VersionId">The draft.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="Reason">Why. Publication demands one.</param>
/// <param name="ExpectedVersion">
/// The token the administrator last read the draft with, so that publishing something somebody else
/// edited in the meantime is refused rather than done.
/// </param>
/// <param name="By">The administrator.</param>
public sealed record PublishCatalogVersionCommand(
    Guid VersionId,
    Guid OrganisationId,
    string Reason,
    EntityTag ExpectedVersion,
    Guid? By);

/// <summary>Retires the published version.</summary>
/// <param name="VersionId">The version.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token of the version the administrator reviewed.</param>
/// <param name="Reason">Why. Retirement demands one.</param>
/// <param name="By">The administrator.</param>
public sealed record RetireCatalogVersionCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    string Reason,
    Guid? By);

/// <summary>Corrects presentation on a published version.</summary>
/// <param name="VersionId">The version.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token of the version the administrator reviewed.</param>
/// <param name="CategoryId">The category to correct, or null when correcting a service type.</param>
/// <param name="ServiceTypeId">The service type to correct, or null when correcting a category.</param>
/// <param name="Presentation">The corrected label, Tamil label, description and display order.</param>
/// <param name="Reason">Why. A correction to a published version demands one.</param>
/// <param name="By">The administrator.</param>
public sealed record CorrectCatalogPresentationCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid? CategoryId,
    Guid? ServiceTypeId,
    CatalogPresentation Presentation,
    string Reason,
    Guid? By);
