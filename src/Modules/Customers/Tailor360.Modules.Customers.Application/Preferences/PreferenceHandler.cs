using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Contracts.Events;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Preferences;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Customers.Application.Preferences;

/// <summary>
/// Reading and replacing how a customer wants to be reached.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Replacing, never patching.</strong> A change carries the whole set of channels, so the
/// audit entry reads as a state rather than as a difference and "which channels does she accept" has
/// one answer a reader of the trail can see without replaying every change since. It is the same
/// reason the role and branch assignments of #25 are replaced rather than added to.
/// </para>
/// <para>
/// <strong>A preference is not a consent.</strong> Allowing a channel here agrees to nothing, and
/// consenting to a purpose does not oblige the shop to use a channel she switched off. Notifications
/// (#47) evaluates both before every send.
/// </para>
/// </remarks>
/// <param name="preferences">The preference store.</param>
/// <param name="customers">The record store, for the customer this is about.</param>
/// <param name="events">This module's event publisher, over this module's outbox.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class PreferenceHandler(
    IPreferenceStore preferences,
    ICustomerStore customers,
    ICustomersEventPublisher events,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A customer's communication preference was recorded or changed.</summary>
    public const string ChangedAction = "customers.preferences.changed";

    /// <summary>
    /// How this customer wants to be reached, or the fact that nobody has asked her.
    /// </summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The preference, or the reason it could not be read.</returns>
    public async Task<Result<AdministeredPreferences>> ReadAsync(
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var customer = await customers.FindAsync(customerId, cancellationToken);

        if (customer is null || customer.OrganisationId != organisationId)
        {
            return Result.Failure<AdministeredPreferences>(CustomersErrors.CustomerNotFound);
        }

        var found = await preferences.FindAsync(customerId, cancellationToken);

        // Nobody has asked her. The language comes from her own record, because she did choose that,
        // and the empty channel set is reported as unrecorded rather than as a decision to allow
        // nothing — the two mean the same for a send and different things for the counter.
        return found is null
            ? Result.Success(AdministeredPreferences.NotRecorded(customerId, customer.Language))
            : Result.Success(
                AdministeredPreferences.From(found, preferences.EntityTagOf(found)));
    }

    /// <summary>Records what the customer now says, replacing whatever was there.</summary>
    /// <param name="command">The whole preference.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The preference as recorded, or the reason it was refused.</returns>
    public async Task<Result<AdministeredPreferences>> ReplaceAsync(
        ReplacePreferencesCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customer = await customers.FindAsync(command.CustomerId, cancellationToken);

        if (customer is null || customer.OrganisationId != command.OrganisationId)
        {
            return Result.Failure<AdministeredPreferences>(CustomersErrors.CustomerNotFound);
        }

        var quiet = QuietHours.TryRead(command.QuietHoursStart, command.QuietHoursEnd);

        if (quiet.IsFailure)
        {
            return Result.Failure<AdministeredPreferences>(quiet.Error);
        }

        var existing = await preferences.FindAsync(command.CustomerId, cancellationToken);
        var before = existing is null ? null : PreferenceTrailEntry.Of(existing);

        if (existing is null)
        {
            var recorded = CommunicationPreferences.Record(
                command.CustomerId,
                command.OrganisationId,
                command.AllowedChannels,
                command.Language,
                quiet.Value,
                clock.UtcNow,
                command.By);

            if (recorded.IsFailure)
            {
                return Result.Failure<AdministeredPreferences>(recorded.Error);
            }

            existing = recorded.Value;
            preferences.Add(existing);
        }
        else
        {
            var replaced = existing.Replace(
                command.AllowedChannels, command.Language, quiet.Value, clock.UtcNow, command.By);

            if (replaced.IsFailure)
            {
                return Result.Failure<AdministeredPreferences>(replaced.Error);
            }
        }

        // Published before the save, so the outbox row and the preference are one save on this
        // module's context and one connection (#77). It carries nothing about the preference itself —
        // channels, language and quiet hours are Personal under data-classification.md section 5.3,
        // whose consumer "reads it through IConsentQuery and never copies it", and an outbox row is a
        // copy. The event says the answer changed; the reader asks what it now is.
        events.Publish(new PreferencesChanged(
            ids.NewId(),
            clock.UtcNow,
            command.CustomerId,
            command.OrganisationId,
            before is null));

        var saved = await preferences.TrySaveChangesAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredPreferences>(saved.Error);
        }

        await PreferenceAudit.RecordAsync(
            audit,
            command.CustomerId,
            before,
            PreferenceTrailEntry.Of(existing),
            cancellationToken);

        return Result.Success(
            AdministeredPreferences.From(existing, preferences.EntityTagOf(existing)));
    }
}

/// <summary>The whole preference, as the customer now states it.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="AllowedChannels">
/// The channels she accepts now. An empty set is a valid answer and means "do not message me"; it does
/// not withdraw consent to anything.
/// </param>
/// <param name="Language">The language to write to her in, or null for the default.</param>
/// <param name="QuietHoursStart">When the quiet window opens in the branch's local time, or null.</param>
/// <param name="QuietHoursEnd">When it closes, or null. Both ends or neither.</param>
/// <param name="By">The member of staff recording it.</param>
public sealed record ReplacePreferencesCommand(
    Guid CustomerId,
    Guid OrganisationId,
    IReadOnlyList<CommunicationChannel>? AllowedChannels,
    string? Language,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    Guid? By);

/// <summary>A customer's preference with the version a change must be made against.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="HasBeenRecorded">False when nobody has recorded a preference for her.</param>
/// <param name="AllowedChannels">The channels she accepts, in a stable order.</param>
/// <param name="Language">The language she is written to in.</param>
/// <param name="QuietHours">The window she would rather not hear from the shop in, or null.</param>
/// <param name="UpdatedAt">When it was last changed, or null when it has never been recorded.</param>
/// <param name="Version">
/// The concurrency token, or null when nothing has been recorded — there is no version of a row that
/// does not exist, and the first write is therefore the one change that needs no precondition.
/// </param>
public sealed record AdministeredPreferences(
    Guid CustomerId,
    bool HasBeenRecorded,
    IReadOnlyList<CommunicationChannel> AllowedChannels,
    string Language,
    QuietHours? QuietHours,
    DateTimeOffset? UpdatedAt,
    EntityTag? Version)
{
    /// <summary>Projects a stored preference.</summary>
    /// <param name="preferences">The preference.</param>
    /// <param name="version">Its concurrency token.</param>
    /// <returns>The record.</returns>
    public static AdministeredPreferences From(
        CommunicationPreferences preferences,
        EntityTag version)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return new AdministeredPreferences(
            preferences.CustomerId,
            HasBeenRecorded: true,
            [.. preferences.AllowedChannels],
            preferences.Language,
            preferences.QuietHours,
            preferences.UpdatedAt,
            version);
    }

    /// <summary>The answer for a customer whose preference nobody has recorded.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="language">The language from her own record.</param>
    /// <returns>The record.</returns>
    public static AdministeredPreferences NotRecorded(Guid customerId, string language)
        => new(customerId, false, [], language, null, null, null);
}
