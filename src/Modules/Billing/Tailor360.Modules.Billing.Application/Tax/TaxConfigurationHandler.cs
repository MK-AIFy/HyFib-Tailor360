using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Tax;

/// <summary>
/// The commands an administrator runs against tax configuration versions (#41, #145).
/// </summary>
/// <remarks>
/// Every command that changes a version compares the caller's <c>If-Match</c> against the version's
/// row version before touching it, saves, and then records an audit entry — the trail may lag reality
/// and must never lead it. Publication runs the checks, retires the version it supersedes in the same
/// transaction, and lets the database settle two administrators publishing at once.
/// </remarks>
public sealed class TaxConfigurationHandler(
    ITaxConfigurationStore store,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A draft was started empty.</summary>
    public const string DraftedAction = "billing.tax_configuration.drafted";

    /// <summary>A draft was started as a copy.</summary>
    public const string ClonedAction = "billing.tax_configuration.cloned";

    /// <summary>A draft's own details changed.</summary>
    public const string ChangedAction = "billing.tax_configuration.changed";

    /// <summary>A draft was published.</summary>
    public const string PublishedAction = "billing.tax_configuration.published";

    /// <summary>A code was added to a draft.</summary>
    public const string TaxCodeAddedAction = "billing.tax_code.added";

    /// <summary>A code of a draft changed.</summary>
    public const string TaxCodeChangedAction = "billing.tax_code.changed";

    /// <summary>A code was removed from a draft.</summary>
    public const string TaxCodeRemovedAction = "billing.tax_code.removed";

    /// <summary>Starts a draft, empty or as a copy of an existing version.</summary>
    public async Task<Result<AdministeredTaxConfiguration>> CreateDraftAsync(
        CreateTaxConfigurationDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var number = await store.NextVersionNumberAsync(command.OrganisationId, cancellationToken);
        var now = clock.UtcNow;

        Result<TaxConfigurationVersion> created;
        string action;
        string summary;

        if (command.CloneFromVersionId is { } sourceId)
        {
            var source = await store.FindAsync(sourceId, command.OrganisationId, cancellationToken);
            if (source is null)
            {
                return Result.Failure<AdministeredTaxConfiguration>(BillingErrors.VersionNotFound);
            }

            created = source.CloneAsDraft(ids, number, command.Name, command.Notes, command.EffectiveFrom, now, command.By);
            action = ClonedAction;
            summary = $"Tax configuration version {number} started as a copy of version {source.VersionNumber}.";
        }
        else
        {
            created = TaxConfigurationVersion.CreateDraft(
                ids.NewId(), command.OrganisationId, number, command.Name, command.Notes, command.EffectiveFrom, now, command.By);
            action = DraftedAction;
            summary = $"Tax configuration version {number} started empty.";
        }

        if (created.IsFailure)
        {
            return Result.Failure<AdministeredTaxConfiguration>(created.Error);
        }

        var draft = created.Value;
        store.Add(draft);

        var saved = await store.SaveDraftAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredTaxConfiguration>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, action, BillingAudit.TaxConfigurationEntity, draft.Id, summary, null, null,
            TaxConfigurationSnapshot.Of(draft), cancellationToken);

        return Result.Success(Administered(draft));
    }

    /// <summary>Changes a draft's name, notes and effective date.</summary>
    public async Task<Result<AdministeredTaxConfiguration>> DescribeAsync(
        DescribeTaxConfigurationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredTaxConfiguration>(found.Error);
        }

        var version = found.Value;
        var before = TaxConfigurationSnapshot.Of(version);
        var described = version.Describe(command.Name, command.Notes, command.EffectiveFrom, clock.UtcNow, command.By);
        if (described.IsFailure)
        {
            return Result.Failure<AdministeredTaxConfiguration>(described.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredTaxConfiguration>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, ChangedAction, BillingAudit.TaxConfigurationEntity, version.Id,
            $"Tax configuration version {version.VersionNumber} described; effective from {version.EffectiveFrom:yyyy-MM-dd}.",
            command.Reason, before, TaxConfigurationSnapshot.Of(version), cancellationToken);

        return Result.Success(Administered(version));
    }

    /// <summary>Adds a tax code to a draft.</summary>
    public async Task<Result<TaxCode>> AddTaxCodeAsync(
        AddTaxCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<TaxCode>(found.Error);
        }

        var version = found.Value;
        var added = version.AddTaxCode(ids.NewId(), ids.NewId(), command.Details, clock.UtcNow, command.By);
        if (added.IsFailure)
        {
            return added;
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<TaxCode>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, TaxCodeAddedAction, BillingAudit.TaxConfigurationEntity, version.Id,
            $"Tax code {added.Value.Code} added to tax configuration version {version.VersionNumber}.",
            command.Reason, null, TaxCodeSnapshot.Of(added.Value), cancellationToken);

        return added;
    }

    /// <summary>Replaces what a draft says about a tax code.</summary>
    public async Task<Result<TaxCode>> EditTaxCodeAsync(
        EditTaxCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<TaxCode>(found.Error);
        }

        var version = found.Value;
        var before = version.FindTaxCode(command.TaxCodeId) is { } existing ? TaxCodeSnapshot.Of(existing) : null;
        var edited = version.EditTaxCode(command.TaxCodeId, command.Details, clock.UtcNow, command.By);
        if (edited.IsFailure)
        {
            return edited;
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<TaxCode>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, TaxCodeChangedAction, BillingAudit.TaxConfigurationEntity, version.Id,
            $"Tax code {edited.Value.Code} of tax configuration version {version.VersionNumber} changed.",
            command.Reason, before, TaxCodeSnapshot.Of(edited.Value), cancellationToken);

        return edited;
    }

    /// <summary>Removes a tax code from a draft.</summary>
    public async Task<Result> RemoveTaxCodeAsync(
        RemoveTaxCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        var version = found.Value;
        var before = version.FindTaxCode(command.TaxCodeId) is { } existing ? TaxCodeSnapshot.Of(existing) : null;
        var removed = version.RemoveTaxCode(command.TaxCodeId, clock.UtcNow, command.By);
        if (removed.IsFailure)
        {
            return removed;
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved;
        }

        await BillingAudit.RecordAsync(
            audit, TaxCodeRemovedAction, BillingAudit.TaxConfigurationEntity, version.Id,
            $"Tax code {before?.Code} removed from tax configuration version {version.VersionNumber}.",
            command.Reason, before, null, cancellationToken);

        return Result.Success();
    }

    /// <summary>Runs the publication checks against a version without publishing it.</summary>
    public async Task<Result<BillingValidationReport>> ValidateAsync(
        Guid versionId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var version = await store.FindAsync(versionId, organisationId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<BillingValidationReport>(BillingErrors.VersionNotFound);
        }

        return Result.Success(await CheckAsync(version, cancellationToken));
    }

    /// <summary>Publishes a draft, retiring the version it supersedes in the same transaction.</summary>
    public async Task<Result<TaxConfigurationPublication>> PublishAsync(
        PublishTaxConfigurationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await LoadForChangeAsync(command.VersionId, command.OrganisationId, command.ExpectedVersion, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<TaxConfigurationPublication>(found.Error);
        }

        var draft = found.Value;
        if (!draft.IsEditable)
        {
            return Result.Failure<TaxConfigurationPublication>(BillingErrors.VersionNotPublishable);
        }

        var report = await CheckAsync(draft, cancellationToken);
        if (report.HasErrors)
        {
            return Result.Failure<TaxConfigurationPublication>(BillingErrors.PublishValidationFailed);
        }

        var outgoing = await store.FindPublishedAsync(command.OrganisationId, cancellationToken);
        var now = clock.UtcNow;
        if (outgoing is not null)
        {
            var superseded = outgoing.Retire(
                now, command.By, $"Superseded by tax configuration version {draft.VersionNumber}: {command.Reason}");
            if (superseded.IsFailure)
            {
                return Result.Failure<TaxConfigurationPublication>(superseded.Error);
            }
        }

        var published = draft.Publish(now, command.By, command.Reason);
        if (published.IsFailure)
        {
            return Result.Failure<TaxConfigurationPublication>(published.Error);
        }

        var saved = await store.SavePublicationAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<TaxConfigurationPublication>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, PublishedAction, BillingAudit.TaxConfigurationEntity, draft.Id,
            $"Tax configuration version {draft.VersionNumber} published with {draft.TaxCodes.Count} tax code(s), "
            + $"effective from {draft.EffectiveFrom:yyyy-MM-dd}"
            + (outgoing is null ? ", as the first published version." : $", superseding version {outgoing.VersionNumber}."),
            command.Reason,
            outgoing is null ? null : TaxConfigurationSnapshot.Of(outgoing),
            TaxConfigurationSnapshot.Of(draft),
            cancellationToken);

        return Result.Success(new TaxConfigurationPublication(Administered(draft), outgoing?.Id, report.Findings));
    }

    private async Task<BillingValidationReport> CheckAsync(TaxConfigurationVersion version, CancellationToken cancellationToken)
    {
        var ledger = await store.ReadCodeHistoryAsync(version.OrganisationId, cancellationToken);
        var published = await store.FindPublishedAsync(version.OrganisationId, cancellationToken);

        return TaxConfigurationPublicationCheck.Run(version, ledger, published);
    }

    private async Task<Result<TaxConfigurationVersion>> LoadForChangeAsync(
        Guid versionId,
        Guid organisationId,
        EntityTag expectedVersion,
        CancellationToken cancellationToken)
    {
        var version = await store.FindAsync(versionId, organisationId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<TaxConfigurationVersion>(BillingErrors.VersionNotFound);
        }

        return expectedVersion.Matches(store.EntityTagOf(version))
            ? Result.Success(version)
            : Result.Failure<TaxConfigurationVersion>(BillingErrors.VersionChanged);
    }

    private AdministeredTaxConfiguration Administered(TaxConfigurationVersion version)
        => new(version, store.EntityTagOf(version));
}

/// <summary>Start a draft.</summary>
public sealed record CreateTaxConfigurationDraftCommand(
    Guid OrganisationId,
    string Name,
    string? Notes,
    DateOnly EffectiveFrom,
    Guid? CloneFromVersionId,
    Guid? By);

/// <summary>Change a draft's own details.</summary>
public sealed record DescribeTaxConfigurationCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    string Name,
    string? Notes,
    DateOnly EffectiveFrom,
    string? Reason,
    Guid? By);

/// <summary>Add a code to a draft.</summary>
public sealed record AddTaxCodeCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    TaxCodeDetails Details,
    string? Reason,
    Guid? By);

/// <summary>Replace a code of a draft.</summary>
public sealed record EditTaxCodeCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid TaxCodeId,
    TaxCodeDetails Details,
    string? Reason,
    Guid? By);

/// <summary>Remove a code from a draft.</summary>
public sealed record RemoveTaxCodeCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    Guid TaxCodeId,
    string? Reason,
    Guid? By);

/// <summary>Publish a draft.</summary>
public sealed record PublishTaxConfigurationCommand(
    Guid VersionId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    string Reason,
    Guid? By);

/// <summary>A version beside the tag a client sends back with its next change.</summary>
public sealed record AdministeredTaxConfiguration(TaxConfigurationVersion Version, EntityTag Tag);

/// <summary>What a publication produced.</summary>
public sealed record TaxConfigurationPublication(
    AdministeredTaxConfiguration Published,
    Guid? SupersededVersionId,
    IReadOnlyList<BillingFinding> Findings);

internal sealed record TaxConfigurationSnapshot(int VersionNumber, string Status, string EffectiveFrom, int TaxCodeCount)
{
    public static TaxConfigurationSnapshot Of(TaxConfigurationVersion version)
        => new(version.VersionNumber, version.Status.ToString(), version.EffectiveFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), version.TaxCodes.Count);
}

internal sealed record TaxCodeSnapshot(string Code, string Classification, string Kind, bool Active, IReadOnlyList<string> Rates)
{
    public static TaxCodeSnapshot Of(TaxCode code)
        => new(
            code.Code,
            code.Classification,
            code.Kind.ToString(),
            code.Active,
            [.. code.Rates.Select(rate => $"{rate.Kind}={rate.RatePercent.ToString(System.Globalization.CultureInfo.InvariantCulture)}")]);
}
