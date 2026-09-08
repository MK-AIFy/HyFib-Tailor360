using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;

namespace Tailor360.Modules.Customers.Application.Abstractions;

/// <summary>
/// Finds customers, without loading them.
/// </summary>
/// <remarks>
/// Separate from <see cref="ICustomerStore"/> because the questions are different: the store loads one
/// aggregate to change it, and this answers "who might this person be" over many rows. Every read here
/// is projected in the database — a counter searching a phone number must not fetch every alias and
/// address in the organisation to render six cards.
/// </remarks>
public interface ICustomerDirectory
{
    /// <summary>Runs a counter search.</summary>
    /// <param name="query">What to look for and how far.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One page of cards.</returns>
    Task<CustomerSearchPage> SearchAsync(
        CustomerSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the records a customer might duplicate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reaches across the whole organisation regardless of branch visibility, on purpose. A record the
    /// creating branch cannot see is exactly the one that produces a duplicate, and
    /// <c>branch-scenarios.md</c> section 3.1 says the search reaches across branches for that reason.
    /// </para>
    /// <para>
    /// A record that has already been merged into another is never a candidate. Offering one would
    /// send somebody to open a record that no longer stands, and merging into it is refused anyway.
    /// </para>
    /// </remarks>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="subject">The record being created, or the one being examined.</param>
    /// <param name="callerBranchIds">
    /// The branches whose sight of a candidate the card should report. A candidate the caller can
    /// already see is a record they can open; one they cannot is a masked disambiguation card. The
    /// search endpoint answers the same question the same way, and a card that always said "not
    /// yours" would have a client offering to open a record that is already on the screen.
    /// </param>
    /// <param name="exceptCustomerId">
    /// A record to leave out — the subject itself, when the subject already exists. Every record is a
    /// perfect match for itself, so asking about an existing customer without this would return it at
    /// the top of its own duplicate list.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The candidates worth showing, strongest first.</returns>
    Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(
        Guid organisationId,
        DuplicateSubject subject,
        IReadOnlyCollection<Guid> callerBranchIds,
        Guid? exceptCustomerId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>What a counter is looking for.</summary>
/// <param name="OrganisationId">The organisation. Never widened by anything the caller sends.</param>
/// <param name="CallerBranchIds">
/// The branches the caller is assigned to. A record visible to one of them is shown in full; one that
/// is not is shown as a masked disambiguation card.
/// </param>
/// <param name="Term">
/// What was typed: digits are matched against the tail of a telephone number, anything else against
/// the folded name, the native name, the customer number and the aliases.
/// </param>
/// <param name="IncludeDeactivated">
/// Whether records withdrawn from ordinary use are included. Off by default, because somebody
/// starting a new order should not be offered one.
/// </param>
/// <param name="Cursor">Where to continue from, opaque.</param>
/// <param name="Limit">How many cards to return.</param>
public sealed record CustomerSearchQuery(
    Guid OrganisationId,
    IReadOnlyCollection<Guid> CallerBranchIds,
    string? Term = null,
    bool IncludeDeactivated = false,
    string? Cursor = null,
    int Limit = CustomerSearchQuery.DefaultLimit)
{
    /// <summary>The page size when the caller does not choose one.</summary>
    public const int DefaultLimit = 20;

    /// <summary>The largest page this search will return.</summary>
    public const int MaximumLimit = 50;

    /// <summary>The fewest characters worth searching on.</summary>
    /// <remarks>
    /// Three, so that a single keystroke does not scan the organisation. Reception types the last four
    /// to six digits of a number, which clears it comfortably.
    /// </remarks>
    public const int MinimumTermLength = 3;
}

/// <summary>One page of search results, and where to continue from.</summary>
/// <param name="Customers">The cards, most recently seen first.</param>
/// <param name="NextCursor">Where the next page starts, or null at the end.</param>
public sealed record CustomerSearchPage(
    IReadOnlyList<CustomerCard> Customers,
    string? NextCursor);

/// <summary>
/// One customer as a search result, before any field-level masking.
/// </summary>
/// <remarks>
/// A card is not a record. It carries what Reception needs to tell two people apart — a name, a
/// masked number, the branch that holds the record and when they last ordered — and nothing else,
/// so that reaching across branches to disambiguate never hands over an address.
/// </remarks>
/// <param name="CustomerId">The record.</param>
/// <param name="CustomerNumber">The display number.</param>
/// <param name="DisplayName">The name as given.</param>
/// <param name="NativeName">The Tamil-script name, where there is one.</param>
/// <param name="MaskedPhone">
/// The number with everything but its last four digits replaced. Enough to confirm a number read
/// aloud, not enough to be a contact list.
/// </param>
/// <param name="OwningBranchId">The branch that created the record.</param>
/// <param name="VisibleToCaller">
/// False when the record is outside the caller's branches. The screen shows those as disambiguation
/// cards, and opening one adds the caller's branch to the record's visibility.
/// </param>
/// <param name="Status">Whether the record is in use.</param>
/// <param name="LastSeenAt">When the record was last changed, which is the closest thing to recency.</param>
public sealed record CustomerCard(
    Guid CustomerId,
    string CustomerNumber,
    string DisplayName,
    string? NativeName,
    string MaskedPhone,
    Guid OwningBranchId,
    bool VisibleToCaller,
    CustomerStatus Status,
    DateTimeOffset LastSeenAt);

/// <summary>A record a new customer might duplicate, and why it looks like one.</summary>
/// <param name="Card">The candidate, as a card.</param>
/// <param name="Match">The confidence and the reasons.</param>
public sealed record DuplicateCandidate(CustomerCard Card, DuplicateMatch Match);
