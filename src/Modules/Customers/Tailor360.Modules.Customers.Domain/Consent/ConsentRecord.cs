using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Consent;

/// <summary>
/// One answer a customer gave about one purpose, at one moment, against one wording version.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This type has no method that changes it, and that is its whole design.</strong>
/// <c>docs/nfr/data-classification.md</c> section 5.3 is explicit: nobody may edit a historical consent
/// record — a change is a new record. So there is no correction, no withdrawal-in-place and no delete.
/// Withdrawing writes a <see cref="ConsentDecision.Withdrawn"/> record; agreeing again afterwards
/// writes another <see cref="ConsentDecision.Granted"/> one. The table is append-only in the database
/// too, protected the way every other append-only table in this system is.
/// </para>
/// <para>
/// The reason is not tidiness. The record <em>is</em> the evidence — of what was agreed, of what was
/// refused, and of the fact that somebody withdrew. Deleting the evidence that a customer withdrew
/// would be exactly the wrong outcome, which is why withdrawal is recorded rather than applied.
/// </para>
/// <para>
/// <strong>What is current is computed, never stored.</strong> The standing answer for a purpose is
/// the most recent record for it; there is no "is current" flag to fall out of step with the rows.
/// </para>
/// </remarks>
public sealed class ConsentRecord
{
    /// <summary>The longest source description the column holds.</summary>
    public const int MaximumSourceLength = 120;

    private ConsentRecord()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private ConsentRecord(
        Guid id,
        Guid organisationId,
        Guid customerId,
        string purposeKey,
        int wordingVersion,
        ConsentDecision decision,
        string source,
        Guid? branchId,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        CustomerId = customerId;
        PurposeKey = purposeKey;
        WordingVersion = wordingVersion;
        Decision = decision;
        Source = source;
        BranchId = branchId;
        RecordedAt = now;
        RecordedBy = by;
    }

    /// <summary>Identity of this record. Media (#31) stores it against an object it relied on.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation. There is exactly one (BR-1).</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The customer who answered.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The purpose, by its stable key.</summary>
    public string PurposeKey { get; private set; } = string.Empty;

    /// <summary>
    /// The wording version the customer was asked under.
    /// </summary>
    /// <remarks>
    /// The member that makes the record evidence rather than a flag. A later wording version does not
    /// re-consent anybody: it leaves this record saying what it always said, against the words that
    /// were actually used.
    /// </remarks>
    public int WordingVersion { get; private set; }

    /// <summary>What they said.</summary>
    public ConsentDecision Decision { get; private set; }

    /// <summary>
    /// How the answer reached the system — "counter, verbal", for instance.
    /// </summary>
    /// <remarks>
    /// Free text within a bound rather than an enumeration, because the ways a shop collects an answer
    /// are a product matter and the documents give one worked example rather than a list. Inventing the
    /// list here would put a taxonomy nobody approved into an evidentiary record.
    /// </remarks>
    public string Source { get; private set; } = string.Empty;

    /// <summary>The branch the answer was taken at, where it was taken at one.</summary>
    public Guid? BranchId { get; private set; }

    /// <summary>When it was recorded.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>The member of staff who recorded it.</summary>
    public Guid? RecordedBy { get; private set; }

    /// <summary>Records one answer.</summary>
    /// <param name="id">The identifier, from the generator.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="customerId">The customer who answered.</param>
    /// <param name="purposeKey">The purpose, by key.</param>
    /// <param name="wordingVersion">The wording version they were asked under.</param>
    /// <param name="decision">What they said.</param>
    /// <param name="source">How the answer reached the system.</param>
    /// <param name="branchId">The branch it was taken at, or null.</param>
    /// <param name="now">The clock.</param>
    /// <param name="by">The member of staff recording it.</param>
    /// <returns>The record, or the reason it was refused.</returns>
    public static Result<ConsentRecord> Record(
        Guid id,
        Guid organisationId,
        Guid customerId,
        string? purposeKey,
        int wordingVersion,
        ConsentDecision decision,
        string? source,
        Guid? branchId,
        DateTimeOffset now,
        Guid? by)
    {
        var key = purposeKey?.Trim();

        if (string.IsNullOrEmpty(key))
        {
            return Result.Failure<ConsentRecord>(CustomersErrors.Required("purposeKey"));
        }

        if (wordingVersion < ConsentWording.FirstVersion)
        {
            // A record with no wording version behind it proves nothing: it says somebody agreed, and
            // not what to.
            return Result.Failure<ConsentRecord>(CustomersErrors.Required("wordingVersion"));
        }

        var given = source?.Trim();

        if (string.IsNullOrEmpty(given))
        {
            return Result.Failure<ConsentRecord>(CustomersErrors.Required("source"));
        }

        return given.Length > MaximumSourceLength
            ? Result.Failure<ConsentRecord>(CustomersErrors.TooLong("source", MaximumSourceLength))
            : Result.Success(new ConsentRecord(
                id, organisationId, customerId, key, wordingVersion, decision, given, branchId, now, by));
    }
}
