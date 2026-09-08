using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Platform.Abstractions.Auditing;

namespace Tailor360.Modules.Customers.Application.Customers;

/// <summary>
/// Writes the Customers module's audit entries.
/// </summary>
/// <remarks>
/// <para>
/// The module's own rows are in the <c>customers</c> schema and the trail is in <c>platform</c>, so
/// they are different contexts and different transactions. The order is fixed and is the house rule:
/// <strong>save the change first, then record it</strong>. The trail may lag reality; it must never
/// lead it, because an entry describing a change that was rolled back is worse than a missing one.
/// </para>
/// <para>
/// <strong>Nothing personal reaches the trail.</strong> A customer's name, telephone number, address
/// and email are Personal data retained for years and read by more people than a log line is
/// (<c>docs/nfr/data-classification.md</c> section 5.2), so the snapshots below carry counts, flags
/// and enum names — never a value. Which fields a correction touched is recorded by <em>name</em>,
/// which is what makes the trail readable without making it a copy of the record.
/// </para>
/// </remarks>
internal static class CustomerAudit
{
    /// <summary>The entity type every Customers entry is recorded against.</summary>
    public const string EntityType = "customers.customer";

    /// <summary>Records one change and commits the entry.</summary>
    /// <param name="audit">The platform's audit writer.</param>
    /// <param name="action">The action constant.</param>
    /// <param name="customerId">The record.</param>
    /// <param name="summary">What happened, in words, naming no value.</param>
    /// <param name="reason">The actor's reason, where the action demands one.</param>
    /// <param name="before">The snapshot before, where there was one.</param>
    /// <param name="after">The snapshot after.</param>
    /// <remarks>
    /// The two snapshots are <see cref="ICustomerAuditState"/> and not <c>object</c>, even though
    /// <c>AuditEntry</c> would take an <c>object</c>. The trail must never become a copy of the record,
    /// and the cheapest way to keep that true is to make the compiler refuse anything that has not been
    /// declared as an audit shape: passing a <c>Customer</c> here does not compile, so nobody can do it
    /// in a hurry.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the entry is committed.</returns>
    public static async Task RecordAsync(
        IAuditWriter audit,
        string action,
        Guid customerId,
        string summary,
        string? reason,
        ICustomerAuditState? before,
        ICustomerAuditState? after,
        CancellationToken cancellationToken)
    {
        await audit.WriteAsync(
            new AuditEntry(action, EntityType, customerId, summary, reason, before, after),
            cancellationToken);

        await audit.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// A customer record as the audit trail describes it: shape, never content.
/// </summary>
/// <param name="Status">Whether the record was in use.</param>
/// <param name="Language">The language, which is a preference and not an identifier.</param>
/// <param name="HasAlternatePhone">Whether a second number was held. Not the number.</param>
/// <param name="HasEmail">Whether an email address was held. Not the address.</param>
/// <param name="HasAddress">Whether a postal address was held. Not the address.</param>
/// <param name="AliasCount">How many aliases were held.</param>
/// <param name="VisibilityBranchCount">How many branches saw the record.</param>
/// <param name="ChangedFields">
/// The names of the fields a correction touched, alphabetically. Names only: "phone" says what a
/// reader of the trail needs and a number would not.
/// </param>
/// <param name="MergedWith">
/// The other record in a merge — the one absorbed, on the survivor's entry; the survivor, on the
/// absorbed record's. An identifier and not a name, which is the same rule the rest of the snapshot
/// follows: the trail says what happened to which records, and a reader who is entitled to know who
/// they were reads the records.
/// </param>
/// <summary>
/// A shape that may be written into the audit trail as a customer's before or after state.
/// </summary>
/// <remarks>
/// A marker with no members. Its whole job is to be a list of the types that have been looked at and
/// judged safe to record — shape and counts, never a value — so that adding a new one is a deliberate
/// act rather than whatever happened to be in scope at the call site.
/// </remarks>
internal interface ICustomerAuditState;

internal sealed record CustomerSnapshot(
    string Status,
    string Language,
    bool HasAlternatePhone,
    bool HasEmail,
    bool HasAddress,
    int AliasCount,
    int VisibilityBranchCount,
    IReadOnlyList<string>? ChangedFields = null,
    Guid? MergedWith = null) : ICustomerAuditState
{
    /// <summary>Takes a snapshot of a customer.</summary>
    /// <param name="customer">The customer.</param>
    /// <param name="changedFields">The fields a correction touched, where one did.</param>
    /// <param name="mergedWith">The other record in a merge, where this entry describes one.</param>
    /// <returns>The snapshot.</returns>
    public static CustomerSnapshot Of(
        Customer customer,
        IReadOnlyList<string>? changedFields = null,
        Guid? mergedWith = null)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return new CustomerSnapshot(
            customer.Status.ToString(),
            customer.Language,
            customer.AlternatePhoneE164 is not null,
            customer.Email is not null,
            customer.AddressLine is not null || customer.Locality is not null,
            customer.Aliases.Count,
            customer.Visibility.Count,
            changedFields,
            mergedWith);
    }
}
