using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Customers.Application.Consent;

/// <summary>
/// Reading a customer's consent, and recording an answer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no withdraw operation, and no correction.</strong> Every answer arrives the same
/// way — <see cref="RecordAsync"/> with a decision — and every one of them appends a row. Withdrawing
/// is a <see cref="ConsentDecision.Withdrawn"/> answer, changing her mind again is another
/// <see cref="ConsentDecision.Granted"/> one, and the standing answer is always the last row rather
/// than a field somebody updated. That is <c>docs/nfr/data-classification.md</c> section 5.3 —
/// "nobody may edit a historical consent record; a change is a new record" — expressed as the only
/// operation there is, rather than as a rule to remember.
/// </para>
/// <para>
/// <strong>An answer is recorded against the wording version current at the moment it is given</strong>,
/// which the handler reads from the register rather than accepting from the caller. A client that
/// could name the version could record an answer against words the customer was never read.
/// </para>
/// </remarks>
/// <param name="consent">The consent register and record store.</param>
/// <param name="customers">The record store, for the customer this is about.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class ConsentHandler(
    IConsentStore consent,
    ICustomerStore customers,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A customer answered about a purpose.</summary>
    public const string RecordedAction = "customers.consent.recorded";

    /// <summary>A customer withdrew an answer she had given.</summary>
    /// <remarks>
    /// A separate action from <see cref="RecordedAction"/>, though both append the same kind of row,
    /// because a withdrawal is what somebody reviewing the trail is looking for. Reading it out of a
    /// generic "consent recorded" entry means reading every entry.
    /// </remarks>
    public const string WithdrawnAction = "customers.consent.withdrawn";

    /// <summary>
    /// Everything this customer has said, purpose by purpose, newest answer first.
    /// </summary>
    /// <remarks>
    /// The whole register comes back, including purposes she has never been asked about and purposes
    /// that have been retired, because the counter screen's job is to show what is outstanding as much
    /// as what was answered. A purpose with no answers is the prompt to ask her.
    /// </remarks>
    /// <param name="customerId">The customer.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The consent state, or the reason it could not be read.</returns>
    public async Task<Result<CustomerConsent>> ReadAsync(
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var customer = await customers.FindAsync(customerId, cancellationToken);

        if (customer is null || customer.OrganisationId != organisationId)
        {
            return Result.Failure<CustomerConsent>(CustomersErrors.CustomerNotFound);
        }

        var register = await consent.ReadRegisterAsync(organisationId, cancellationToken);
        var records = await consent.ReadRecordsAsync(customerId, cancellationToken);

        // The store returns them newest first and this keeps that order rather than re-sorting.
        // Grouping preserves the order within each group, and the sort belongs in SQL: PostgreSQL
        // orders a uuid by its bytes while .NET's Guid comparison walks its fields in a different
        // order, so a second sort here could put the screen and IConsentQuery on different answers.
        var byPurpose = records
            .GroupBy(record => record.PurposeKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ConsentAnswer>)[.. group.Select(ConsentAnswer.From)],
                StringComparer.Ordinal);

        var purposes = register
            .Select(purpose => new ConsentPurposeState(
                purpose.Key,
                purpose.Name,
                purpose.Description,
                purpose.IsRetired,
                purpose.CurrentWordingVersion,
                byPurpose.TryGetValue(purpose.Key, out var answers) ? answers : []))
            .ToList();

        // An answer against a purpose the register no longer carries is still the customer's answer,
        // and losing it here would make the screen quietly disagree with the evidence. It cannot
        // happen through this handler — recording checks the register first — but a purpose could be
        // removed by an operator, and a screen that silently dropped rows would be the worse failure.
        var orphaned = byPurpose
            .Where(entry => !register.Any(purpose => string.Equals(
                purpose.Key, entry.Key, StringComparison.Ordinal)))
            .Select(entry => new ConsentPurposeState(entry.Key, entry.Key, null, true, 0, entry.Value));

        return Result.Success(new CustomerConsent([.. purposes, .. orphaned]));
    }

    /// <summary>Records one answer, whatever the answer is.</summary>
    /// <param name="command">Who answered, about what, and what they said.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The answer as recorded, or the reason it was refused.</returns>
    public async Task<Result<ConsentAnswer>> RecordAsync(
        RecordConsentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customer = await customers.FindAsync(command.CustomerId, cancellationToken);

        if (customer is null || customer.OrganisationId != command.OrganisationId)
        {
            return Result.Failure<ConsentAnswer>(CustomersErrors.CustomerNotFound);
        }

        var key = ConsentPurposeKeys.Read(command.PurposeKey, "purposeKey");

        if (key.IsFailure)
        {
            return Result.Failure<ConsentAnswer>(key.Error);
        }

        var purpose = await consent.FindPurposeAsync(
            command.OrganisationId, key.Value, cancellationToken);

        if (purpose is null)
        {
            return Result.Failure<ConsentAnswer>(CustomersErrors.ConsentPurposeNotFound(key.Value));
        }

        // A retired purpose is not asked about any more. What she already said stands and is still
        // read back; a new answer to a question the shop has stopped asking is refused rather than
        // filed.
        if (purpose.IsRetired)
        {
            return Result.Failure<ConsentAnswer>(CustomersErrors.ConsentPurposeRetired(purpose.Key));
        }

        if (purpose.CurrentWordingVersion < ConsentWording.FirstVersion)
        {
            return Result.Failure<ConsentAnswer>(
                CustomersErrors.ConsentWordingNotPublished(purpose.Key));
        }

        var previous = await StandingAnswerAsync(command.CustomerId, purpose.Key, cancellationToken);

        var recorded = ConsentRecord.Record(
            ids.NewId(),
            command.OrganisationId,
            command.CustomerId,
            purpose.Key,
            purpose.CurrentWordingVersion,
            command.Decision,
            command.Source,
            command.BranchId,
            clock.UtcNow,
            command.By);

        if (recorded.IsFailure)
        {
            return Result.Failure<ConsentAnswer>(recorded.Error);
        }

        consent.Add(recorded.Value);

        var saved = await consent.SaveChangesAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<ConsentAnswer>(saved.Error);
        }

        var answer = ConsentAnswer.From(recorded.Value);

        await ConsentAudit.RecordAsync(
            audit,
            command.Decision is ConsentDecision.Withdrawn ? WithdrawnAction : RecordedAction,
            command.CustomerId,
            $"Consent for '{purpose.Key}' recorded as {command.Decision} against wording version "
            + $"{purpose.CurrentWordingVersion}.",
            previous,
            answer,
            cancellationToken);

        return Result.Success(answer);
    }

    private async Task<ConsentAnswer?> StandingAnswerAsync(
        Guid customerId,
        string purposeKey,
        CancellationToken cancellationToken)
    {
        var records = await consent.ReadRecordsAsync(customerId, cancellationToken);

        // Newest first already, decided in SQL. Re-sorting here is the thing not to do.
        var latest = records
            .FirstOrDefault(record => string.Equals(
                record.PurposeKey, purposeKey, StringComparison.Ordinal));

        return latest is null ? null : ConsentAnswer.From(latest);
    }
}

/// <summary>An answer to record.</summary>
/// <param name="CustomerId">The customer who answered.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="PurposeKey">The purpose, by key.</param>
/// <param name="Decision">What she said.</param>
/// <param name="Source">
/// How the answer reached the system — "counter, verbal", for instance. Required, because an answer
/// whose provenance nobody recorded is weaker evidence than one that says where it came from.
/// </param>
/// <param name="BranchId">The branch it was taken at, where it was taken at one.</param>
/// <param name="By">The member of staff recording it.</param>
public sealed record RecordConsentCommand(
    Guid CustomerId,
    Guid OrganisationId,
    string? PurposeKey,
    ConsentDecision Decision,
    string? Source,
    Guid? BranchId,
    Guid? By);

/// <summary>Everything a customer has said, purpose by purpose.</summary>
/// <param name="Purposes">
/// The organisation's register, each with that customer's answers newest first, followed by any
/// purpose she has answered that the register no longer carries.
/// </param>
public sealed record CustomerConsent(IReadOnlyList<ConsentPurposeState> Purposes);

/// <summary>One purpose and where this customer stands on it.</summary>
/// <param name="Key">The stable key.</param>
/// <param name="Name">The name a counter screen shows.</param>
/// <param name="Description">What agreeing to it allows.</param>
/// <param name="IsRetired">True once the purpose is no longer asked about.</param>
/// <param name="CurrentWordingVersion">
/// The highest wording version published, or zero when none has been — in which case no answer can be
/// recorded, because there are no words to read her.
/// </param>
/// <param name="Answers">
/// Every answer she has given about this purpose, newest first. Empty when nobody has asked, which is
/// how the screen knows to.
/// </param>
public sealed record ConsentPurposeState(
    string Key,
    string Name,
    string? Description,
    bool IsRetired,
    int CurrentWordingVersion,
    IReadOnlyList<ConsentAnswer> Answers);

/// <summary>One answer, as the application layer hands it back.</summary>
/// <param name="RecordId">The record. Media stores it against an object it relied on.</param>
/// <param name="PurposeKey">The purpose she was answering about.</param>
/// <param name="Decision">What she said.</param>
/// <param name="WordingVersion">The wording version she was asked under.</param>
/// <param name="RecordedAt">When it was recorded.</param>
/// <param name="Source">How the answer reached the system.</param>
/// <param name="RecordedBy">The member of staff who recorded it.</param>
/// <param name="BranchId">The branch it was taken at.</param>
public sealed record ConsentAnswer(
    Guid RecordId,
    string PurposeKey,
    ConsentDecision Decision,
    int WordingVersion,
    DateTimeOffset RecordedAt,
    string Source,
    Guid? RecordedBy,
    Guid? BranchId)
{
    /// <summary>Projects a stored record.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The answer.</returns>
    public static ConsentAnswer From(ConsentRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new ConsentAnswer(
            record.Id,
            record.PurposeKey,
            record.Decision,
            record.WordingVersion,
            record.RecordedAt,
            record.Source,
            record.RecordedBy,
            record.BranchId);
    }
}
