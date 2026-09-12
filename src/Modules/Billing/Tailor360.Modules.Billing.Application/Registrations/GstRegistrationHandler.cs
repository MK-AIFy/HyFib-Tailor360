using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Registrations;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Registrations;

/// <summary>The commands an administrator runs against GST registrations (#41, #145).</summary>
/// <remarks>
/// At most one registration of a branch is in force on any day. The handler checks it against what
/// it can read, and the database's exclusion constraint settles the race the read cannot see; both
/// answer the same conflict.
/// </remarks>
public sealed class GstRegistrationHandler(
    IGstRegistrationStore store,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A registration was recorded.</summary>
    public const string AddedAction = "billing.gst_registration.added";

    /// <summary>A registration was amended.</summary>
    public const string ChangedAction = "billing.gst_registration.changed";

    /// <summary>Records a registration.</summary>
    public async Task<Result<AdministeredRegistration>> AddAsync(
        AddGstRegistrationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var created = GstRegistration.Create(ids.NewId(), command.OrganisationId, command.Details, clock.UtcNow, command.By);
        if (created.IsFailure)
        {
            return Result.Failure<AdministeredRegistration>(created.Error);
        }

        var registration = created.Value;
        var overlapping = await OverlapsAsync(registration, cancellationToken);
        if (overlapping)
        {
            return Result.Failure<AdministeredRegistration>(BillingErrors.RegistrationOverlaps);
        }

        store.Add(registration);
        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredRegistration>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, AddedAction, BillingAudit.RegistrationEntity, registration.Id,
            $"GST registration recorded for branch {registration.BranchId:N}, effective from {registration.EffectiveFrom:yyyy-MM-dd}.",
            command.Reason, null, RegistrationSnapshot.Of(registration), cancellationToken);

        return Result.Success(new AdministeredRegistration(registration, store.EntityTagOf(registration)));
    }

    /// <summary>Amends a registration.</summary>
    public async Task<Result<AdministeredRegistration>> AmendAsync(
        AmendGstRegistrationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var registration = await store.FindAsync(command.RegistrationId, command.OrganisationId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure<AdministeredRegistration>(BillingErrors.RegistrationNotFound);
        }

        if (!command.ExpectedVersion.Matches(store.EntityTagOf(registration)))
        {
            return Result.Failure<AdministeredRegistration>(BillingErrors.RegistrationChanged);
        }

        var before = RegistrationSnapshot.Of(registration);
        var amended = registration.Amend(command.Details, clock.UtcNow, command.By);
        if (amended.IsFailure)
        {
            return Result.Failure<AdministeredRegistration>(amended.Error);
        }

        if (await OverlapsAsync(registration, cancellationToken))
        {
            return Result.Failure<AdministeredRegistration>(BillingErrors.RegistrationOverlaps);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredRegistration>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, ChangedAction, BillingAudit.RegistrationEntity, registration.Id,
            $"GST registration of branch {registration.BranchId:N} amended.",
            command.Reason, before, RegistrationSnapshot.Of(registration), cancellationToken);

        return Result.Success(new AdministeredRegistration(registration, store.EntityTagOf(registration)));
    }

    private async Task<bool> OverlapsAsync(GstRegistration registration, CancellationToken cancellationToken)
    {
        var siblings = await store.ListForBranchAsync(registration.BranchId, registration.OrganisationId, cancellationToken);

        return siblings.Any(other => other.Id != registration.Id
                                     && other.Overlaps(registration.EffectiveFrom, registration.EffectiveTo));
    }
}

/// <summary>Record a registration.</summary>
public sealed record AddGstRegistrationCommand(Guid OrganisationId, GstRegistrationDetails Details, string? Reason, Guid? By);

/// <summary>Amend a registration.</summary>
public sealed record AmendGstRegistrationCommand(
    Guid RegistrationId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    GstRegistrationDetails Details,
    string? Reason,
    Guid? By);

/// <summary>A registration beside the tag a client sends back with its next change.</summary>
public sealed record AdministeredRegistration(GstRegistration Registration, EntityTag Tag);

/// <summary>What the audit trail records about a registration: never the GSTIN itself.</summary>
internal sealed record RegistrationSnapshot(Guid BranchId, string StateCode, string EffectiveFrom, string? EffectiveTo)
{
    public static RegistrationSnapshot Of(GstRegistration registration)
        => new(
            registration.BranchId,
            registration.StateCode,
            registration.EffectiveFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            registration.EffectiveTo?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
}
