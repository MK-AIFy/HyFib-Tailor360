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
/// <strong>The organisation is checked here; branch reach is not.</strong> The organisation is the
/// tenant boundary and is never the caller's to assert past (ADR-0007 keeps multi-tenancy an additive
/// change, which means nothing may be built that would hand one organisation's record to another), so
/// a customer looked up under the wrong organisation reads as not found — indistinguishably from one
/// that does not exist. Within the organisation a customer record is organisation-wide and belongs to
/// no one branch (<c>docs/prd/workflows/branch-scenarios.md</c> section 3.2), so there is no branch for
/// this query to refuse against; the endpoint that authorised the caller has already taken that decision.
/// </para>
/// </remarks>
public interface ICustomerSnapshotQuery
{
    /// <summary>
    /// The facts about one customer that a document may carry, masked to what the caller may see.
    /// </summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="organisationId">The organisation the caller is acting within. A record outside it is not found.</param>
    /// <param name="callerPermissions">
    /// The permission keys the caller holds. Only <see cref="CustomerSnapshot.ContactPermission"/> is
    /// read; the rest are ignored, so a caller may pass its whole set without filtering it first.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot, or null when no record in that organisation has that identity.</returns>
    Task<CustomerSnapshot?> GetAsync(
        Guid customerId,
        Guid organisationId,
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
/// <param name="MergedIntoCustomerId">
/// The record this one was folded into, or null while it stands on its own.
/// <para>
/// A consumer holding a customer identifier can be holding one that has since been merged — an order
/// placed last year names the record that took it — and this is how the answer says so without the
/// consumer having to have heard <c>customers.customer-merged.v1</c>. What to do with it depends on
/// what the consumer is showing: a snapshot frozen onto an invoice keeps naming the record it named
/// (INV-CUS-04), while a screen offering to start something new should follow the pointer.
/// </para>
/// <para>
/// Added within v1 as an optional trailing parameter, so every existing caller compiles and every
/// existing construction still means what it did.
/// </para>
/// </param>
/// <param name="IsActive">
/// Whether the record is still in ordinary use. False once it has been deactivated: still readable,
/// still named by every order that referenced it, but "not offered when somebody is starting something
/// new" (<c>CustomerStatus.Deactivated</c>'s own words) — a consumer starting new work refuses it, while
/// one printing a document about existing work carries on. Added within v1 the same way as
/// <paramref name="MergedIntoCustomerId"/>, defaulting to the answer every construction before it meant.
/// </param>
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
    string? Postcode,
    Guid? MergedIntoCustomerId = null,
    bool IsActive = true)
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
