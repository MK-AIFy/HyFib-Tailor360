using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;

namespace Tailor360.Modules.Customers.Api.Payloads;

/// <summary>
/// One customer record on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The contact fields are nullable and are populated only for a caller holding
/// <c>customers.read_contact</c>. That split is <c>docs/nfr/data-classification.md</c> section 5.2 —
/// a Tailor sees a name on a job card and never a telephone number — and it is applied in exactly one
/// place, <see cref="From"/>, so a field added later cannot arrive on a screen that was never meant
/// to carry it.
/// </para>
/// <para>
/// The platform's declared response-view mechanism is the eventual home for this, and
/// <c>docs/security/field-visibility.md</c> section 1 names #26 as the issue that adds the customer
/// views. It is not used here because declaring one means changing a test whose name says "workshop
/// surface" and whose body iterates every view, and a sentence the owner is being asked to approve.
/// </para>
/// </remarks>
/// <param name="CustomerId">The record.</param>
/// <param name="CustomerNumber">The display number, as it appears on a card.</param>
/// <param name="DisplayName">The name as the customer gave it.</param>
/// <param name="NativeName">The Tamil-script name, where there is one.</param>
/// <param name="Phone">The primary telephone number, or null without <c>customers.read_contact</c>.</param>
/// <param name="AlternatePhone">The second number, under the same permission.</param>
/// <param name="Email">The email address, under the same permission.</param>
/// <param name="AddressLine">The street line, under the same permission.</param>
/// <param name="Locality">The area or town, under the same permission.</param>
/// <param name="Postcode">The postal code, under the same permission.</param>
/// <param name="Language">The language the customer is written to in.</param>
/// <param name="Status">Whether the record is in use.</param>
/// <param name="OwningBranchId">The branch that created the record.</param>
/// <param name="VisibilityBranchIds">The branches that see it in ordinary search results.</param>
/// <param name="Aliases">Previous names, spellings and merged numbers.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="UpdatedAt">When it was last changed.</param>
/// <param name="Version">The concurrency token an edit must be made against.</param>
public sealed record CustomerPayload(
    Guid CustomerId,
    string CustomerNumber,
    string DisplayName,
    string? NativeName,
    string? Phone,
    string? AlternatePhone,
    string? Email,
    string? AddressLine,
    string? Locality,
    string? Postcode,
    string Language,
    string Status,
    Guid OwningBranchId,
    IReadOnlyList<Guid> VisibilityBranchIds,
    IReadOnlyList<CustomerAliasPayload> Aliases,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version)
{
    /// <summary>Projects a record onto the wire, withholding contact fields where they are not held.</summary>
    /// <param name="customer">The record.</param>
    /// <param name="mayReadContact">Whether the caller holds <c>customers.read_contact</c>.</param>
    /// <returns>The payload.</returns>
    public static CustomerPayload From(AdministeredCustomer customer, bool mayReadContact)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return new CustomerPayload(
            customer.CustomerId,
            customer.CustomerNumber,
            customer.DisplayName,
            customer.NativeName,
            mayReadContact ? customer.PhoneE164 : null,
            mayReadContact ? customer.AlternatePhoneE164 : null,
            mayReadContact ? customer.Email : null,
            mayReadContact ? customer.AddressLine : null,
            mayReadContact ? customer.Locality : null,
            mayReadContact ? customer.Postcode : null,
            customer.Language,
            customer.Status.ToString(),
            customer.OwningBranchId,
            customer.VisibilityBranchIds,
            [.. customer.Aliases.Select(CustomerAliasPayload.From)],
            customer.CreatedAt,
            customer.UpdatedAt,
            customer.Version.Version);
    }
}

/// <summary>One alias held against a customer.</summary>
/// <param name="Kind">Why it is held: a previous name, a spelling, or a merged customer number.</param>
/// <param name="Value">The alias as it was written.</param>
/// <param name="RecordedAt">When it was recorded.</param>
public sealed record CustomerAliasPayload(string Kind, string Value, DateTimeOffset RecordedAt)
{
    /// <summary>Projects an alias.</summary>
    /// <param name="alias">The alias.</param>
    /// <returns>The payload.</returns>
    public static CustomerAliasPayload From(CustomerAliasView alias)
    {
        ArgumentNullException.ThrowIfNull(alias);

        return new CustomerAliasPayload(alias.Kind.ToString(), alias.Value, alias.RecordedAt);
    }
}

/// <summary>
/// One customer as a search result.
/// </summary>
/// <remarks>
/// A card carries a <em>masked</em> number for everybody, whatever they hold. It is enough to confirm
/// a number a customer is reading out and not enough to be a contact list, which is what makes it safe
/// to answer a search that reaches across branches — the reach that stops a second record being
/// created for somebody who already exists (<c>branch-scenarios.md</c> section 3.1).
/// </remarks>
/// <param name="CustomerId">The record.</param>
/// <param name="CustomerNumber">The display number.</param>
/// <param name="DisplayName">The name as given.</param>
/// <param name="NativeName">The Tamil-script name, where there is one.</param>
/// <param name="MaskedPhone">The number with everything but its last four digits replaced.</param>
/// <param name="OwningBranchId">The branch that created the record.</param>
/// <param name="VisibleToCaller">
/// False when the record is outside the caller's branches. The screen shows those as disambiguation
/// cards; opening one adds the caller's branch to the record's visibility and is audited.
/// </param>
/// <param name="Status">Whether the record is in use.</param>
/// <param name="LastSeenAt">When the record was last changed.</param>
public sealed record CustomerCardPayload(
    Guid CustomerId,
    string CustomerNumber,
    string DisplayName,
    string? NativeName,
    string MaskedPhone,
    Guid OwningBranchId,
    bool VisibleToCaller,
    string Status,
    DateTimeOffset LastSeenAt)
{
    /// <summary>Projects a card.</summary>
    /// <param name="card">The card.</param>
    /// <returns>The payload.</returns>
    public static CustomerCardPayload From(CustomerCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new CustomerCardPayload(
            card.CustomerId,
            card.CustomerNumber,
            card.DisplayName,
            card.NativeName,
            card.MaskedPhone,
            card.OwningBranchId,
            card.VisibleToCaller,
            card.Status.ToString(),
            card.LastSeenAt);
    }
}

/// <summary>One page of search results.</summary>
/// <param name="Customers">The cards, most recently seen first.</param>
/// <param name="NextCursor">Where the next page starts, or null at the end.</param>
public sealed record CustomerPagePayload(
    IReadOnlyList<CustomerCardPayload> Customers,
    string? NextCursor)
{
    /// <summary>Projects a page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The payload.</returns>
    public static CustomerPagePayload From(CustomerSearchPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new CustomerPagePayload(
            [.. page.Customers.Select(CustomerCardPayload.From)],
            page.NextCursor);
    }
}

/// <summary>A record the new customer might duplicate, and why.</summary>
/// <param name="Customer">The candidate, as a card.</param>
/// <param name="Confidence">How strongly it resembles: <c>High</c>, <c>Medium</c> or <c>Low</c>.</param>
/// <param name="Reasons">
/// What matched, strongest first, as names a screen turns into a sentence — never shown as codes.
/// </param>
public sealed record DuplicateCandidatePayload(
    CustomerCardPayload Customer,
    string Confidence,
    IReadOnlyList<string> Reasons)
{
    /// <summary>Projects a candidate.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <returns>The payload.</returns>
    public static DuplicateCandidatePayload From(DuplicateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return new DuplicateCandidatePayload(
            CustomerCardPayload.From(candidate.Card),
            candidate.Match.Confidence.ToString(),
            [.. candidate.Match.Reasons.Select(reason => reason.ToString())]);
    }
}

/// <summary>What the caller must read before creating a record that resembles one already held.</summary>
/// <param name="Candidates">The records to read, strongest resemblance first.</param>
public sealed record DuplicateReviewPayload(IReadOnlyList<DuplicateCandidatePayload> Candidates);

/// <summary>What to create.</summary>
/// <remarks>Every member is nullable: validation is the server's and answers in this module's codes.</remarks>
/// <param name="DisplayName">The name as the customer gives it.</param>
/// <param name="NativeName">The Tamil-script name, where the customer gives one.</param>
/// <param name="Phone">The primary telephone number, however it is written.</param>
/// <param name="AlternatePhone">A second number.</param>
/// <param name="Email">An email address.</param>
/// <param name="AddressLine">The street line.</param>
/// <param name="Locality">The area or town.</param>
/// <param name="Postcode">The postal code.</param>
/// <param name="Language">The language to write to the customer in. Defaults to <c>en-IN</c>.</param>
/// <param name="DuplicatesReviewed">
/// True when the caller has read the candidates the first attempt returned and says this is somebody
/// new. The decision is theirs and the trail records that they took it.
/// </param>
public sealed record RegisterCustomerRequest(
    string? DisplayName,
    string? NativeName,
    string? Phone,
    string? AlternatePhone,
    string? Email,
    string? AddressLine,
    string? Locality,
    string? Postcode,
    string? Language,
    bool DuplicatesReviewed = false);

/// <summary>What to correct, and why.</summary>
/// <param name="DisplayName">The name.</param>
/// <param name="NativeName">The Tamil-script name.</param>
/// <param name="Phone">The primary telephone number.</param>
/// <param name="AlternatePhone">A second number.</param>
/// <param name="Email">An email address.</param>
/// <param name="AddressLine">The street line.</param>
/// <param name="Locality">The area or town.</param>
/// <param name="Postcode">The postal code.</param>
/// <param name="Language">The language to write to the customer in.</param>
/// <param name="Reason">Why the record is being corrected. Required.</param>
public sealed record CorrectCustomerRequest(
    string? DisplayName,
    string? NativeName,
    string? Phone,
    string? AlternatePhone,
    string? Email,
    string? AddressLine,
    string? Locality,
    string? Postcode,
    string? Language,
    string? Reason);

/// <summary>A command whose whole content is the reason for it.</summary>
/// <param name="Reason">Why. Required.</param>
public sealed record CustomerReasonRequest(string? Reason);

/// <summary>The bounds and codes the customer endpoints answer with.</summary>
public static class CustomerRequests
{
    /// <summary>The code a stale <c>If-Match</c> is refused with.</summary>
    public const string VersionConflict = "customers.version-conflict";

    /// <summary>What that refusal says.</summary>
    public const string VersionConflictDetail =
        "Somebody else changed this customer while you had it open. Read it again, check what changed, "
        + "and make the correction against the version you can see.";
}
