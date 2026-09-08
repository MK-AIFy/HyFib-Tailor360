namespace Tailor360.Modules.Customers.Contracts.Customers;

/// <summary>
/// What another module may copy onto a document about a customer.
/// </summary>
/// <remarks>
/// <para>
/// Orders (#32a) takes a snapshot onto an estimate and an order; Billing (#42) takes one onto an
/// invoice. Both need the same handful of facts and neither may read <c>customers.customers</c>
/// (<c>docs/architecture/module-ownership.md</c> section 5.2), so this is the one way those facts
/// leave the module.
/// </para>
/// <para>
/// <strong>The caller's permissions decide what comes back, and the module applies them.</strong>
/// <c>docs/nfr/data-classification.md</c> section 5.2 splits reading a customer from reading their
/// contact details, and the whole point of the split is that a Tailor opening a job card sees a name
/// and never a telephone number. Leaving the mask to each consumer would mean writing it four times
/// and getting it right four times, so the mask is applied here — the caller passes what it holds and
/// receives what it may see.
/// </para>
/// <para>
/// <strong>Reach is not checked here</strong>, deliberately. A customer record is organisation-wide
/// and belongs to no one branch (<c>docs/prd/workflows/branch-scenarios.md</c> section 3.2), so there
/// is no branch for this query to refuse against; the endpoint that authorised the caller has already
/// taken that decision, and the identifier it passes is a UUIDv7 rather than anything guessable.
/// </para>
/// </remarks>
public interface ICustomerSnapshotQuery
{
    /// <summary>
    /// The facts about one customer that a document may carry, masked to what the caller may see.
    /// </summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="callerPermissions">
    /// The permission keys the caller holds. Only <see cref="CustomerSnapshot.ContactPermission"/> is
    /// read; the rest are ignored, so a caller may pass its whole set without filtering it first.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot, or null when no record has that identity.</returns>
    Task<CustomerSnapshot?> GetAsync(
        Guid customerId,
        IReadOnlyCollection<string> callerPermissions,
        CancellationToken cancellationToken = default);
}

/// <summary>One customer as a document copies them.</summary>
/// <param name="CustomerId">The record. A UUIDv7, and the only identifier that crosses the boundary.</param>
/// <param name="CustomerNumber">
/// The display number, <c>C-&lt;branch&gt;-000001</c>. Stable for the life of the record, which is what
/// lets an invoice printed last year still name the customer it was printed for.
/// </param>
/// <param name="DisplayName">The name as it was given.</param>
/// <param name="NativeName">The Tamil-script name, where there is one.</param>
/// <param name="Language">The language the customer is written to in.</param>
/// <param name="OwningBranchId">The branch that created the record.</param>
/// <param name="ContactIncluded">
/// Whether the contact fields below were populated. False when the caller does not hold
/// <see cref="ContactPermission"/>, which is a different thing from the customer having no address —
/// and the difference matters, because an invoice printed with a blank address because the caller was
/// masked is a defect, while one printed with a blank address because there is none is a fact.
/// </param>
/// <param name="PhoneE164">The primary telephone number, or null when the contact fields are masked.</param>
/// <param name="AlternatePhoneE164">The second telephone number, or null when masked or absent.</param>
/// <param name="Email">The email address, or null when masked or absent.</param>
/// <param name="AddressLine">The street line, or null when masked or absent.</param>
/// <param name="Locality">The area or town, or null when masked or absent.</param>
/// <param name="Postcode">The postal code, or null when masked or absent.</param>
public sealed record CustomerSnapshot(
    Guid CustomerId,
    string CustomerNumber,
    string DisplayName,
    string? NativeName,
    string Language,
    Guid OwningBranchId,
    bool ContactIncluded,
    string? PhoneE164,
    string? AlternatePhoneE164,
    string? Email,
    string? AddressLine,
    string? Locality,
    string? Postcode)
{
    /// <summary>
    /// The permission a caller must hold for the contact fields to be populated.
    /// </summary>
    /// <remarks>
    /// The same key as <c>Tailor360.Platform.Security.Permissions.CustomersPermissions.ReadContact</c>,
    /// written out a second time because a <c>Contracts</c> project may reference
    /// <c>Platform.Abstractions</c> and nothing else. A unit test asserts the two are the same string,
    /// so the copy cannot drift into a key that matches nothing and masks everybody.
    /// </remarks>
    public const string ContactPermission = "customers.read_contact";

    /// <summary>Whether a caller holding these permissions may see the contact fields.</summary>
    /// <param name="callerPermissions">The permission keys the caller holds.</param>
    /// <returns>True when the contact fields may be populated.</returns>
    public static bool MayReadContact(IReadOnlyCollection<string>? callerPermissions)
        => callerPermissions is not null
            && callerPermissions.Contains(ContactPermission, StringComparer.Ordinal);
}
