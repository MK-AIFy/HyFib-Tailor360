using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Contracts.Events;
using Tailor360.Modules.Customers.Domain.Measurements;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Customers.Application.Measurements;

/// <summary>
/// Administering measurement templates: drafting, reviewing, publishing and retiring them.
/// </summary>
/// <remarks>
/// <para>
/// Every command that changes anything writes an audit entry, and the ones that change what a customer's
/// measurements will be read under demand a reason. The trail is the answer to "why does this field say something
/// different from what the tailor remembers", which is a question a shop asks about a template far more often than
/// it asks anything else.
/// </para>
/// </remarks>
/// <param name="store">The module's template store.</param>
/// <param name="clock">The platform clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="users">Identity's staff directory, for the separation-of-duties count.</param>
/// <param name="catalogue">Catalog's availability contract, for the retirement guard.</param>
/// <param name="events">This module's outbox, for the two lifecycle events Catalog reconciles on.</param>
public sealed class MeasurementTemplateHandler(
    IMeasurementTemplateStore store,
    IClock clock,
    IIdGenerator ids,
    IAuditWriter audit,
    IUserDirectory users,
    ICatalogAvailabilityQuery catalogue,
    ICustomersEventPublisher events)
{
    /// <summary>A template was created.</summary>
    public const string TemplateCreatedAction = "customers.measurement_template.created";

    /// <summary>A draft version was started.</summary>
    public const string DraftedAction = "customers.measurement_template.version.drafted";

    /// <summary>A draft was started as a copy of another version.</summary>
    public const string ClonedAction = "customers.measurement_template.version.cloned";

    /// <summary>A field was added to a draft.</summary>
    public const string FieldAddedAction = "customers.measurement_template.field.added";

    /// <summary>A field of a draft was changed.</summary>
    public const string FieldChangedAction = "customers.measurement_template.field.changed";

    /// <summary>A field was removed from a draft.</summary>
    public const string FieldRemovedAction = "customers.measurement_template.field.removed";

    /// <summary>A draft was submitted for review.</summary>
    public const string SubmittedAction = "customers.measurement_template.version.submitted";

    /// <summary>A version in review was sent back to its author.</summary>
    public const string ReturnedAction = "customers.measurement_template.version.returned";

    /// <summary>A version was approved by a second administrator.</summary>
    public const string ApprovedAction = "customers.measurement_template.version.approved";

    /// <summary>A version became the one measurements are captured against.</summary>
    public const string PublishedAction = "customers.measurement_template.version.published";

    /// <summary>A version stopped taking new captures.</summary>
    public const string RetiredAction = "customers.measurement_template.version.retired";

    /// <summary>Creates a template with no versions.</summary>
    /// <param name="command">What to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The template, or the reason it was refused.</returns>
    public async Task<Result<AdministeredTemplate>> CreateAsync(
        CreateMeasurementTemplateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var created = MeasurementTemplate.Create(
            ids.NewId(), command.OrganisationId, command.Code, command.Name, command.Description,
            clock.UtcNow, command.By);

        if (created.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(created.Error);
        }

        store.Add(created.Value);

        var saved = await store.SaveNewAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(saved.Error);
        }

        await MeasurementTemplateAudit.RecordAsync(
            audit,
            TemplateCreatedAction,
            created.Value.Id,
            $"Measurement template '{created.Value.Code}' created. It captures nothing until a version is drafted, "
            + "reviewed and published.",
            null,
            null,
            null,
            cancellationToken);

        return Result.Success(Administered(created.Value, null));
    }

    /// <summary>Starts a draft version.</summary>
    /// <param name="command">What to start and from where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public async Task<Result<AdministeredTemplate>> StartDraftAsync(
        StartTemplateDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var located = await LoadAsync(command.TemplateId, command.OrganisationId, cancellationToken);

        if (located.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(located.Error);
        }

        var template = located.Value;
        var draft = template.StartDraft(
            ids, command.Name, command.Notes, command.DefaultDisplayUnit, command.CloneFromVersionId,
            clock.UtcNow, command.By);

        if (draft.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(draft.Error);
        }

        var saved = await store.SaveNewAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(saved.Error);
        }

        await MeasurementTemplateAudit.RecordAsync(
            audit,
            command.CloneFromVersionId is null ? DraftedAction : ClonedAction,
            template.Id,
            command.CloneFromVersionId is null
                ? $"Version {draft.Value.VersionNumber} of '{template.Code}' started empty."
                : $"Version {draft.Value.VersionNumber} of '{template.Code}' started as a copy of an "
                  + "existing version.",
            null,
            null,
            TemplateVersionSnapshot.Of(draft.Value),
            cancellationToken);

        return Result.Success(Administered(template, draft.Value));
    }

    /// <summary>Adds a field to a draft, or replaces one.</summary>
    /// <param name="command">The field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason the change was refused.</returns>
    public async Task<Result<AdministeredTemplate>> SaveFieldAsync(
        SaveTemplateFieldCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var located = await LoadVersionAsync(
            command.TemplateId, command.VersionId, command.OrganisationId, command.ExpectedVersion,
            cancellationToken);

        if (located.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(located.Error);
        }

        var (template, version) = located.Value;
        var before = TemplateVersionSnapshot.Of(version);

        var saved = command.FieldId is { } fieldId
            ? version.EditField(fieldId, command.Definition, clock.UtcNow, command.By)
            : version.AddField(ids.NewId(), command.Definition, clock.UtcNow, command.By);

        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(saved.Error);
        }

        template.Touch(clock.UtcNow, command.By);

        var committed = await store.SaveAsync(cancellationToken);

        if (committed.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(committed.Error);
        }

        await MeasurementTemplateAudit.RecordAsync(
            audit,
            command.FieldId is null ? FieldAddedAction : FieldChangedAction,
            template.Id,
            $"Field '{saved.Value.Key}' {(command.FieldId is null ? "added to" : "changed in")} version "
            + $"{version.VersionNumber} of '{template.Code}'.",
            null,
            before,
            TemplateVersionSnapshot.Of(version),
            cancellationToken);

        return Result.Success(Administered(template, version));
    }

    /// <summary>Removes a field from a draft.</summary>
    /// <param name="command">Which field, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason the removal was refused.</returns>
    public async Task<Result<AdministeredTemplate>> RemoveFieldAsync(
        RemoveTemplateFieldCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var located = await LoadVersionAsync(
            command.TemplateId, command.VersionId, command.OrganisationId, command.ExpectedVersion,
            cancellationToken);

        if (located.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(located.Error);
        }

        var (template, version) = located.Value;
        var before = TemplateVersionSnapshot.Of(version);
        var key = version.Fields.SingleOrDefault(field => field.Id == command.FieldId)?.Key.Value;

        var removed = version.RemoveField(command.FieldId, clock.UtcNow, command.By);

        if (removed.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(removed.Error);
        }

        template.Touch(clock.UtcNow, command.By);

        var committed = await store.SaveAsync(cancellationToken);

        if (committed.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(committed.Error);
        }

        await MeasurementTemplateAudit.RecordAsync(
            audit,
            FieldRemovedAction,
            template.Id,
            $"Field '{key}' removed from version {version.VersionNumber} of '{template.Code}'.",
            command.Reason,
            before,
            TemplateVersionSnapshot.Of(version),
            cancellationToken);

        return Result.Success(Administered(template, version));
    }

    /// <summary>Submits a draft for a second administrator to review.</summary>
    /// <param name="command">Which version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason it could not be submitted.</returns>
    public Task<Result<AdministeredTemplate>> SubmitAsync(
        TemplateLifecycleCommand command,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            command,
            (version, now, by) => version.Submit(now, by),
            SubmittedAction,
            (template, version) =>
                $"Version {version.VersionNumber} of '{template.Code}' submitted for review.",
            reasonRequired: false,
            cancellationToken);

    /// <summary>Sends a version in review back to its author.</summary>
    /// <param name="command">Which version, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason it could not be returned.</returns>
    public Task<Result<AdministeredTemplate>> ReturnToDraftAsync(
        TemplateLifecycleCommand command,
        CancellationToken cancellationToken = default)
        => TransitionAsync(
            command,
            (version, now, by) => version.ReturnToDraft(now, by),
            ReturnedAction,
            (template, version) =>
                $"Version {version.VersionNumber} of '{template.Code}' returned to draft. Its approval was "
                + "dropped with it, so it is reviewed again in the state it is then in.",
            reasonRequired: true,
            cancellationToken);

    /// <summary>Approves a reviewed version.</summary>
    /// <param name="command">Which version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason it could not be approved.</returns>
    public async Task<Result<AdministeredTemplate>> ApproveAsync(
        TemplateLifecycleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // "The submitter does not also approve" is only a rule a shop can follow when there is somebody else to
        // ask. Counting the administrators who could have approved is what turns it from a rule that locks a
        // one-owner shop out of its own templates into one that applies exactly when it can be met.
        var administrators = await users.CountActiveWithPermissionAsync(
            CustomersPermissions.PublishTemplates, command.OrganisationId, cancellationToken);

        return await TransitionAsync(
            command,
            (version, now, by) => version.Approve(now, by, soleAdministrator: administrators <= 1),
            ApprovedAction,
            (template, version) => administrators <= 1
                ? $"Version {version.VersionNumber} of '{template.Code}' approved by its author, who is the only "
                  + "administrator able to approve it."
                : $"Version {version.VersionNumber} of '{template.Code}' approved.",
            reasonRequired: false,
            cancellationToken);
    }

    /// <summary>Makes a version the one measurements are captured against.</summary>
    /// <param name="command">Which version, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason publication was refused.</returns>
    public async Task<Result<AdministeredTemplate>> PublishAsync(
        TemplateLifecycleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var located = await LoadVersionAsync(
            command.TemplateId, command.VersionId, command.OrganisationId, command.ExpectedVersion,
            cancellationToken);

        if (located.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(located.Error);
        }

        var (template, version) = located.Value;

        if (TemplateValidation.HasErrors(TemplateValidation.Validate(version)))
        {
            return Result.Failure<AdministeredTemplate>(MeasurementErrors.PublishValidationFailed);
        }

        var before = TemplateVersionSnapshot.Of(version);
        var published = template.PublishVersion(
            command.VersionId, clock.UtcNow, command.By, command.Reason);

        if (published.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(published.Error);
        }

        // Staged before the save, so the event commits with the publication or not at all. Catalog
        // reconciles INV-MTV-06 on it: publishing a version is what closes a breach that a retirement
        // opened, and nothing else would tell Catalog the template can be measured against again.
        events.Publish(new MeasurementTemplateVersionPublished(
            ids.NewId(),
            clock.UtcNow,
            template.Id,
            template.OrganisationId,
            version.Id,
            template.Code,
            version.VersionNumber,
            published.Value?.Id));

        var saved = await store.SavePublicationAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(saved.Error);
        }

        await MeasurementTemplateAudit.RecordAsync(
            audit,
            PublishedAction,
            template.Id,
            published.Value is { } superseded
                ? $"Version {version.VersionNumber} of '{template.Code}' published, superseding version "
                  + $"{superseded.VersionNumber}. Measurements already taken still render through the version "
                  + "they were captured under."
                : $"Version {version.VersionNumber} of '{template.Code}' published. It is the first version, so "
                  + "this template can now be measured against.",
            command.Reason,
            before,
            TemplateVersionSnapshot.Of(version),
            cancellationToken);

        return Result.Success(Administered(template, version));
    }

    /// <summary>Stops new captures against the published version.</summary>
    /// <param name="command">Which version, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or the reason retirement was refused.</returns>
    public async Task<Result<AdministeredTemplate>> RetireAsync(
        TemplateLifecycleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var located = await LoadVersionAsync(
            command.TemplateId, command.VersionId, command.OrganisationId, command.ExpectedVersion,
            cancellationToken);

        if (located.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(located.Error);
        }

        var (template, version) = located.Value;

        // Retiring the published version leaves this template with none, so anything the catalogue offers against
        // it becomes unorderable. Refused rather than allowed with a warning: the counter would discover it.
        if (version.Status == TemplateStatus.Published
            && await catalogue.ReferencesMeasurementTemplateAsync(
                template.Id, command.OrganisationId, cancellationToken))
        {
            return Result.Failure<AdministeredTemplate>(MeasurementErrors.RetirementWouldStrandOrders);
        }

        return await TransitionAsync(
            command,
            (candidate, now, by) => candidate.Retire(now, by, command.Reason),
            RetiredAction,
            (owner, candidate) =>
                $"Version {candidate.VersionNumber} of '{owner.Code}' retired. Nothing new is captured against it; "
                + "everything already captured still renders through it.",
            reasonRequired: true,
            cancellationToken,
            // Staged before the save, so the event commits with the retirement or not at all. This is the
            // write that can strand a published catalogue (INV-MTV-06): the guard above asked Catalog and
            // then wrote here, and a catalogue publication committing in that window was validated against
            // a template this retirement has since emptied. Catalog reconciles on the event.
            (owner, candidate) => events.Publish(new MeasurementTemplateVersionRetired(
                ids.NewId(),
                clock.UtcNow,
                owner.Id,
                owner.OrganisationId,
                candidate.Id,
                owner.Code,
                candidate.VersionNumber,
                owner.PublishedVersion is not null)));
    }

    /// <summary>Checks a version without changing anything.</summary>
    /// <param name="templateId">The template.</param>
    /// <param name="versionId">The version.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report, or the reason it could not be produced.</returns>
    public async Task<Result<TemplateValidationReport>> ValidateAsync(
        Guid templateId,
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var located = await LoadVersionAsync(templateId, versionId, organisationId, null, cancellationToken);

        return located.IsFailure
            ? Result.Failure<TemplateValidationReport>(located.Error)
            : Result.Success(
                new TemplateValidationReport(versionId, TemplateValidation.Validate(located.Value.Version)));
    }

    /// <summary>Reads one template and every version of it.</summary>
    /// <param name="templateId">The template.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The template, or the reason it could not be read.</returns>
    public async Task<Result<AdministeredTemplate>> ReadAsync(
        Guid templateId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var located = await LoadAsync(templateId, organisationId, cancellationToken);

        return located.IsFailure
            ? Result.Failure<AdministeredTemplate>(located.Error)
            : Result.Success(Administered(located.Value, located.Value.PublishedVersion));
    }

    /// <summary>Every template in the organisation.</summary>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The templates, in code order.</returns>
    public async Task<IReadOnlyList<AdministeredTemplate>> ListAsync(
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var templates = await store.ListAsync(organisationId, cancellationToken);

        return [.. templates.Select(template => Administered(template, template.PublishedVersion))];
    }

    /// <param name="onTransitioned">
    /// Staged after the transition succeeds and before the save, for a transition that also publishes an
    /// integration event. It runs inside the same unit of work on purpose: an event staged after the save would be
    /// a dual write, which is the thing the outbox exists to make impossible.
    /// </param>
    private async Task<Result<AdministeredTemplate>> TransitionAsync(
        TemplateLifecycleCommand command,
        Func<TemplateVersion, DateTimeOffset, Guid?, Result> transition,
        string action,
        Func<MeasurementTemplate, TemplateVersion, string> summary,
        bool reasonRequired,
        CancellationToken cancellationToken,
        Action<MeasurementTemplate, TemplateVersion>? onTransitioned = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (reasonRequired && string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<AdministeredTemplate>(MeasurementErrors.Required("reason"));
        }

        var located = await LoadVersionAsync(
            command.TemplateId, command.VersionId, command.OrganisationId, command.ExpectedVersion,
            cancellationToken);

        if (located.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(located.Error);
        }

        var (template, version) = located.Value;
        var before = TemplateVersionSnapshot.Of(version);

        var moved = transition(version, clock.UtcNow, command.By);

        if (moved.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(moved.Error);
        }

        template.Touch(clock.UtcNow, command.By);
        onTransitioned?.Invoke(template, version);

        var committed = await store.SaveAsync(cancellationToken);

        if (committed.IsFailure)
        {
            return Result.Failure<AdministeredTemplate>(committed.Error);
        }

        await MeasurementTemplateAudit.RecordAsync(
            audit,
            action,
            template.Id,
            summary(template, version),
            string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason,
            before,
            TemplateVersionSnapshot.Of(version),
            cancellationToken);

        return Result.Success(Administered(template, version));
    }

    private async Task<Result<MeasurementTemplate>> LoadAsync(
        Guid templateId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var template = await store.FindAsync(templateId, organisationId, cancellationToken);

        return template is null
            ? Result.Failure<MeasurementTemplate>(MeasurementErrors.TemplateNotFound)
            : Result.Success(template);
    }

    private async Task<Result<(MeasurementTemplate Template, TemplateVersion Version)>> LoadVersionAsync(
        Guid templateId,
        Guid versionId,
        Guid organisationId,
        Tailor360.Platform.Abstractions.Concurrency.EntityTag? expected,
        CancellationToken cancellationToken)
    {
        var located = await LoadAsync(templateId, organisationId, cancellationToken);

        if (located.IsFailure)
        {
            return Result.Failure<(MeasurementTemplate, TemplateVersion)>(located.Error);
        }

        var template = located.Value;

        if (expected is { } tag && !tag.Matches(store.EntityTagOf(template)))
        {
            return Result.Failure<(MeasurementTemplate, TemplateVersion)>(MeasurementErrors.VersionChanged);
        }

        return template.Find(versionId) is not { } version
            ? Result.Failure<(MeasurementTemplate, TemplateVersion)>(MeasurementErrors.VersionNotFound)
            : Result.Success((template, version));
    }

    private AdministeredTemplate Administered(MeasurementTemplate template, TemplateVersion? version)
        => new(template, version, store.EntityTagOf(template));
}
