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
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the entry is committed.</returns>
    public static async Task RecordAsync(
        IAuditWriter audit,
        string action,
        Guid customerId,
        string summary,
        string? reason,
        CustomerSnapshot? before,
        CustomerSnapshot? after,
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
internal sealed record CustomerSnapshot(
    string Status,
    string Language,
    bool HasAlternatePhone,
    bool HasEmail,
    bool HasAddress,
    int AliasCount,
    int VisibilityBranchCount,
    IReadOnlyList<string>? ChangedFields = null)
{
    /// <summary>Takes a snapshot of a customer.</summary>
    /// <param name="customer">The customer.</param>
    /// <param name="changedFields">The fields a correction touched, where one did.</param>
    /// <returns>The snapshot.</returns>
    public static CustomerSnapshot Of(Customer customer, IReadOnlyList<string>? changedFields = null)
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
            changedFields);
    }
}
