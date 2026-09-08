using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;
using Tailor360.Modules.Customers.Domain.Naming;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// The counter search, and the duplicate lookup that runs before a record is created.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Branch reach is enforced here, in the query, and nowhere else.</strong> The platform's
/// resource-scope mechanism decides one identifier taken from a route, so a search reaches its
/// handler with no branch decision taken — <c>docs/security/permission-matrix.md</c> section 7 records
/// that the collection half is unbuilt and owned by #32a, and warns against writing the first list
/// endpoint believing the question is answered. This query is that first endpoint, so it answers the
/// question itself: every read is bounded by the caller's organisation, and every card says whether
/// the caller's branches can see the record.
/// </para>
/// <para>
/// A record outside the caller's branches is <em>not</em> hidden. It comes back as a masked
/// disambiguation card — name, masked number, owning branch, last seen — because
/// <c>branch-scenarios.md</c> section 3.1 is explicit that the search reaches across branches on
/// purpose: the alternative is a second record for a customer who already exists, and the remedy for
/// that is an irreversible merge.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class CustomerDirectory(CustomersDbContext context) : ICustomerDirectory
{
    /// <summary>How many trailing digits of a number stay readable on a masked card.</summary>
    private const int VisibleTailDigits = 4;

    /// <inheritdoc />
    public async Task<CustomerSearchPage> SearchAsync(
        CustomerSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var term = query.Term?.Trim() ?? string.Empty;
        if (term.Length < CustomerSearchQuery.MinimumTermLength)
        {
            return new CustomerSearchPage([], null);
        }

        var rows = context.Customers
            .AsNoTracking()
            .Where(customer => customer.OrganisationId == query.OrganisationId);

        if (!query.IncludeDeactivated)
        {
            rows = rows.Where(customer => customer.Status == CustomerStatus.Active);
        }

        rows = OnlyDigits(term) is { Length: >= CustomerSearchQuery.MinimumTermLength } digits
            ? rows.Where(customer =>
                EF.Functions.Like(customer.PhoneLastSix, "%" + digits)
                || (customer.AlternatePhoneLastSix != null
                    && EF.Functions.Like(customer.AlternatePhoneLastSix, "%" + digits))
                || EF.Functions.ILike(customer.CustomerNumber, "%" + digits + "%"))
            : ByName(rows, term);

        if (Decode(query.Cursor) is var (seenAt, lastId) && lastId != Guid.Empty)
        {
            rows = rows.Where(customer =>
                customer.UpdatedAt < seenAt
                || (customer.UpdatedAt == seenAt && customer.Id.CompareTo(lastId) < 0));
        }

        var callerBranches = query.CallerBranchIds.ToHashSet();

        var page = await rows
            .OrderByDescending(customer => customer.UpdatedAt)
            .ThenByDescending(customer => customer.Id)
            .Take(query.Limit + 1)
            .Select(customer => new Row(
                customer.Id,
                customer.CustomerNumber,
                customer.DisplayName,
                customer.NativeName,
                customer.PhoneE164,
                customer.OwningBranchId,
                customer.Status,
                customer.UpdatedAt,
                customer.Visibility.Any(visibility => callerBranches.Contains(visibility.BranchId))))
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > query.Limit;
        var found = page.Take(query.Limit).ToList();

        return new CustomerSearchPage(
            [.. found.Select(ToCard)],
            hasMore && found.Count > 0 ? Encode(found[^1].UpdatedAt, found[^1].Id) : null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(
        Guid organisationId,
        DuplicateSubject subject,
        IReadOnlyCollection<Guid> callerBranchIds,
        Guid? exceptCustomerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(callerBranchIds);

        var callerBranches = callerBranchIds.ToHashSet();

        // Everything that could possibly resemble the record, fetched once and scored in memory. The
        // alternative — scoring in SQL — would put the rules somewhere they cannot be unit-tested and
        // would still have to fetch the same rows to explain itself.
        //
        // Every field the scoring reads is projected, including the ones that only ever weaken or
        // strengthen a match. Leaving the address out and passing null in its place made
        // DuplicateConfidence.Medium unreachable: "same name, same place" scored as Low, so the
        // create screen never blocked on it, and EX-01's inline warning was quietly weaker than the
        // rule it implements.
        var candidates = await context.Customers
            .AsNoTracking()
            .Where(customer =>
                customer.OrganisationId == organisationId
                // A record that has already been merged is not offered. Merging into one is refused,
                // and opening one sends somebody to a record that no longer stands.
                && customer.MergedIntoCustomerId == null
                && (exceptCustomerId == null || customer.Id != exceptCustomerId)
                && (customer.PhoneE164 == subject.PhoneE164
                    || customer.AlternatePhoneE164 == subject.PhoneE164
                    || (subject.AlternatePhoneE164 != null
                        && (customer.PhoneE164 == subject.AlternatePhoneE164
                            || customer.AlternatePhoneE164 == subject.AlternatePhoneE164))
                    || (subject.NormalisedName != "" && customer.NormalisedName == subject.NormalisedName)
                    || (subject.NativeName != null && customer.NativeName == subject.NativeName)))
            .OrderByDescending(customer => customer.UpdatedAt)
            .Take(MaximumCandidates)
            .Select(customer => new CandidateRow(
                new Row(
                    customer.Id,
                    customer.CustomerNumber,
                    customer.DisplayName,
                    customer.NativeName,
                    customer.PhoneE164,
                    customer.OwningBranchId,
                    customer.Status,
                    customer.UpdatedAt,
                    // Answered the same way the search answers it. A candidate the caller can already
                    // see is one they can open; saying "not yours" about every candidate would have a
                    // client offer to open a record that is already in front of them, and would
                    // misdescribe the very case — a record visible here — that a merge screen exists
                    // to resolve.
                    customer.Visibility.Any(visibility => callerBranches.Contains(visibility.BranchId))),
                customer.NormalisedName,
                customer.AlternatePhoneE164,
                customer.Locality,
                customer.Postcode))
            .ToListAsync(cancellationToken);

        var scored = new List<DuplicateCandidate>(candidates.Count);

        foreach (var candidate in candidates)
        {
            // The stored key, not the display name folded again. Two implementations of one rule
            // disagree the first time the normaliser changes, and they disagree in favour of the copy
            // nobody indexed.
            var existing = new DuplicateSubject(
                candidate.NormalisedName,
                candidate.Card.NativeName,
                candidate.Card.PhoneE164,
                candidate.AlternatePhoneE164,
                candidate.Locality,
                candidate.Postcode);

            var match = DuplicateScoring.Compare(subject, existing);

            if (match.Confidence is not DuplicateConfidence.None)
            {
                scored.Add(new DuplicateCandidate(ToCard(candidate.Card), match));
            }
        }

        return [.. scored.OrderByDescending(candidate => candidate.Match.Confidence)];
    }

    /// <summary>The most candidates a create will ever compare against.</summary>
    /// <remarks>
    /// A bound, not a page: a customer whose name is shared by four hundred people in the organisation
    /// must not make the create screen fetch four hundred rows. The strongest matches are the most
    /// recently seen, which is what somebody at a counter is most likely to be looking at.
    /// </remarks>
    private const int MaximumCandidates = 25;

    private static IQueryable<Customer> ByName(IQueryable<Customer> rows, string term)
    {
        var folded = CustomerNameNormaliser.Normalise(term);
        var pattern = "%" + (folded.Length > 0 ? folded : term) + "%";

        return rows.Where(customer =>
            EF.Functions.ILike(customer.NormalisedName, pattern)
            || (customer.NativeName != null && EF.Functions.ILike(customer.NativeName, "%" + term + "%"))
            || customer.Aliases.Any(alias => EF.Functions.ILike(alias.NormalisedValue, pattern)));
    }

    private static CustomerCard ToCard(Row row) => new(
        row.Id,
        row.CustomerNumber,
        row.DisplayName,
        row.NativeName,
        Mask(row.PhoneE164),
        row.OwningBranchId,
        row.VisibleToCaller,
        row.Status,
        row.UpdatedAt);

    /// <summary>
    /// Everything but the last four digits, so a number read aloud can be confirmed and a list of
    /// cards is not a contact list.
    /// </summary>
    private static string Mask(string e164)
    {
        if (e164.Length <= VisibleTailDigits)
        {
            return new string('*', e164.Length);
        }

        return string.Concat(
            new string('*', e164.Length - VisibleTailDigits),
            e164.AsSpan(e164.Length - VisibleTailDigits));
    }

    private static string? OnlyDigits(string term)
    {
        var builder = new StringBuilder(term.Length);

        foreach (var character in term)
        {
            if (char.IsAsciiDigit(character))
            {
                builder.Append(character);
            }
            else if (character is not (' ' or '-' or '+' or '(' or ')' or '.'))
            {
                // A letter anywhere means this is a name, not a number.
                return null;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string Encode(DateTimeOffset updatedAt, Guid id)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{updatedAt:O}|{id}")));

    /// <summary>
    /// Reads a cursor, answering "start at the beginning" for anything it cannot read.
    /// </summary>
    /// <remarks>
    /// Defensive on purpose: a truncated or edited cursor is a first page, not a 400. It carries no
    /// authority — the organisation filter is applied whatever it says.
    /// </remarks>
    private static (DateTimeOffset UpdatedAt, Guid Id) Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (default, Guid.Empty);
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');

            return parts.Length == 2
                && DateTimeOffset.TryParse(
                    parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt)
                && Guid.TryParse(parts[1], out var id)
                ? (updatedAt, id)
                : (default, Guid.Empty);
        }
        catch (FormatException)
        {
            return (default, Guid.Empty);
        }
    }

    private sealed record Row(
        Guid Id,
        string CustomerNumber,
        string DisplayName,
        string? NativeName,
        string PhoneE164,
        Guid OwningBranchId,
        CustomerStatus Status,
        DateTimeOffset UpdatedAt,
        bool VisibleToCaller);

    /// <summary>
    /// A candidate row: the card a counter is shown, and the fields only the scoring reads.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Row"/> so the ordinary search does not fetch four columns it never
    /// looks at. Search runs on every keystroke; duplicate detection runs twice per customer.
    /// </remarks>
    private sealed record CandidateRow(
        Row Card,
        string NormalisedName,
        string? AlternatePhoneE164,
        string? Locality,
        string? Postcode);
}
