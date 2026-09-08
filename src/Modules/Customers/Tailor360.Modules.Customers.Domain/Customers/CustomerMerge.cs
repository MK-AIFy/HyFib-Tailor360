using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Customers;

/// <summary>
/// The record of one irreversible, authorised decision that two customer records are one person.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is evidence, not state.</strong> The state is on the two customer rows —
/// <see cref="Customer.MergedIntoCustomerId"/> and the alias holding the merged number. This is the
/// answer to "who decided this, when, on what grounds, and what did it actually do", which
/// <c>docs/prd/exceptions.md</c> EX-01 requires of a decision that cannot be undone. So the table is
/// append-only, enforced by a trigger, and carries no concurrency token: there is nothing to overwrite.
/// </para>
/// <para>
/// <strong>The counts are what happened, not what was asked for.</strong> Two records written under
/// one name record a single alias; two records already visible to the same branches add no visibility.
/// A reader comparing this row against the aggregate a year later needs to be able to tell those cases
/// apart, and re-deriving them from the aggregate is not possible once a later correction has moved it
/// on.
/// </para>
/// <para>
/// <strong><see cref="Reason"/> is nullable, and only for one reason.</strong> A reason is required to
/// perform a merge and the application refuses one without it. The column admits null so that the
/// erasure workflow (#57) can redact free text a member of staff typed about a person, without
/// deleting the evidence that the merge happened. Nothing else may clear it, and the trigger the
/// migration installs permits exactly that one change and no other.
/// </para>
/// </remarks>
public sealed class CustomerMerge
{
    /// <summary>The longest reason the column holds.</summary>
    /// <remarks>
    /// The same bound the correction reason uses. It is stated here as well because the
    /// <c>Domain</c> project cannot reference the <c>Application</c> project that holds the other
    /// copy, and a merge record that would not fit the reason its own command accepted is a defect
    /// that only shows up in production.
    /// </remarks>
    public const int MaximumReasonLength = 500;

    private CustomerMerge()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CustomerMerge(
        Guid id,
        Guid organisationId,
        Guid survivorCustomerId,
        Guid mergedCustomerId,
        string mergedCustomerNumber,
        string? reason,
        Guid? branchId,
        MergeAbsorption absorption,
        Guid eventId,
        DateTimeOffset mergedAt,
        Guid? mergedBy)
    {
        Id = id;
        OrganisationId = organisationId;
        SurvivorCustomerId = survivorCustomerId;
        MergedCustomerId = mergedCustomerId;
        MergedCustomerNumber = mergedCustomerNumber;
        Reason = reason;
        BranchId = branchId;
        NumberAliasId = absorption.NumberAliasId;
        AliasesRecorded = absorption.AliasesRecorded;
        VisibilityBranchesAdded = absorption.VisibilityBranchesAdded;
        EventId = eventId;
        MergedAt = mergedAt;
        MergedBy = mergedBy;
    }

    /// <summary>Identity of the merge decision. A UUIDv7.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation both records belong to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The record that survived and now answers for the person.</summary>
    public Guid SurvivorCustomerId { get; private set; }

    /// <summary>The record that was folded in.</summary>
    public Guid MergedCustomerId { get; private set; }

    /// <summary>
    /// The display number the merged record carried, copied here so the decision reads on its own.
    /// </summary>
    /// <remarks>
    /// A customer number is a display number rather than personal data — it names a record, not a
    /// person — and it is what somebody holding an old receipt will quote. Keeping it here means the
    /// merge record answers "which number went away" without joining to a row that a later erasure may
    /// have emptied.
    /// </remarks>
    public string MergedCustomerNumber { get; private set; } = string.Empty;

    /// <summary>Why the two records were judged to be one person. Null only after redaction.</summary>
    public string? Reason { get; private set; }

    /// <summary>The branch the decision was taken at, where the session was working in one.</summary>
    public Guid? BranchId { get; private set; }

    /// <summary>The alias on the survivor now holding the merged customer number.</summary>
    public Guid NumberAliasId { get; private set; }

    /// <summary>How many aliases the survivor gained: one or two.</summary>
    public int AliasesRecorded { get; private set; }

    /// <summary>How many branches gained sight of the survivor because of this merge.</summary>
    public int VisibilityBranchesAdded { get; private set; }

    /// <summary>
    /// The integration event this merge published, so the decision and the message about it can be
    /// matched up from either end when a subscriber asks what it was told.
    /// </summary>
    public Guid EventId { get; private set; }

    /// <summary>When the decision was taken, in UTC.</summary>
    public DateTimeOffset MergedAt { get; private set; }

    /// <summary>Who took it. Null only where no session did, which no endpoint permits.</summary>
    public Guid? MergedBy { get; private set; }

    /// <summary>
    /// Records one merge decision.
    /// </summary>
    /// <param name="id">Identity of the decision, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation both records belong to.</param>
    /// <param name="survivor">The record that survives, after it has absorbed the other.</param>
    /// <param name="merged">The record that was folded in.</param>
    /// <param name="reason">Why they are one person.</param>
    /// <param name="branchId">The branch the decision was taken at, where there was one.</param>
    /// <param name="absorption">What the absorption actually recorded.</param>
    /// <param name="eventId">The identity of the integration event published for it.</param>
    /// <param name="mergedAt">The instant, from <c>IClock</c>.</param>
    /// <param name="mergedBy">The actor.</param>
    /// <returns>The record, or the reason it was refused.</returns>
    public static Result<CustomerMerge> Record(
        Guid id,
        Guid organisationId,
        Customer survivor,
        Customer merged,
        string? reason,
        Guid? branchId,
        MergeAbsorption absorption,
        Guid eventId,
        DateTimeOffset mergedAt,
        Guid? mergedBy)
    {
        ArgumentNullException.ThrowIfNull(survivor);
        ArgumentNullException.ThrowIfNull(merged);

        if (id == Guid.Empty)
        {
            return Result.Failure<CustomerMerge>(CustomersErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<CustomerMerge>(CustomersErrors.Required("organisationId"));
        }

        if (eventId == Guid.Empty)
        {
            return Result.Failure<CustomerMerge>(CustomersErrors.Required("eventId"));
        }

        var trimmed = reason?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<CustomerMerge>(CustomersErrors.ReasonRequired);
        }

        if (trimmed.Length > MaximumReasonLength)
        {
            return Result.Failure<CustomerMerge>(
                CustomersErrors.TooLong("reason", MaximumReasonLength));
        }

        return Result.Success(new CustomerMerge(
            id,
            organisationId,
            survivor.Id,
            merged.Id,
            merged.CustomerNumber,
            trimmed,
            branchId,
            absorption,
            eventId,
            mergedAt,
            mergedBy));
    }
}
