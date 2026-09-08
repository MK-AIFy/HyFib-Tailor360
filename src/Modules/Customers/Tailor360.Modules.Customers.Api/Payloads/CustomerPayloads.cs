using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Application.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;
using Tailor360.Platform.Abstractions.Concurrency;

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
/// <param name="MergedIntoCustomerId">
/// The record this one was folded into, or null while it stands on its own. Never masked: whether the
/// record still stands is a fact about the record rather than about the person, and a screen that
/// cannot see it offers actions against a customer who no longer exists.
/// </param>
/// <param name="MergedAt">When it was folded in, or null while it stands on its own.</param>
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
    string Version,
    Guid? MergedIntoCustomerId = null,
    DateTimeOffset? MergedAt = null)
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
            customer.Version.Version,
            // Never masked by customers.read_contact. Whether the record still stands is a fact about
            // the record rather than about the person, and a client that cannot see it shows somebody
            // who has been merged away as though she were current.
            customer.MergedIntoCustomerId,
            customer.MergedAt);
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

/// <summary>The records that resemble one already held: read before creating, and before merging.</summary>
/// <param name="Candidates">The records to read, strongest resemblance first.</param>
public sealed record DuplicateReviewPayload(IReadOnlyList<DuplicateCandidatePayload> Candidates)
{
    /// <summary>Wraps a scored list.</summary>
    /// <param name="candidates">The candidates, strongest first.</param>
    /// <returns>The payload.</returns>
    public static DuplicateReviewPayload From(IReadOnlyList<DuplicateCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return new DuplicateReviewPayload([.. candidates.Select(DuplicateCandidatePayload.From)]);
    }
}

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

/// <summary>
/// The decision that the record named in the path and the one named here are the same person.
/// </summary>
/// <remarks>
/// The survivor is the path and not the body, deliberately: the record that carries on is the one
/// whose version the caller sent in <c>If-Match</c>, and putting it in the body would make it possible
/// to send a precondition for one record and a merge for another.
/// </remarks>
/// <param name="MergedCustomerId">The record to fold in. It will not survive.</param>
/// <param name="MergedCustomerVersion">
/// The <c>version</c> of that record as the caller read it, from its own <c>GET</c>. Required, and
/// checked inside the row lock alongside the <c>If-Match</c> on the survivor.
/// <para>
/// A merge approves a <em>pair</em>: these two records, as they read on the screen, are one person.
/// An <c>If-Match</c> alone protects one half of that — and the half it leaves open is the record
/// about to stop existing, so a correction to its name, number or address would otherwise be merged
/// away with no undo. It costs the client a read of the record it is about to fold in, which is not
/// much to ask before an irreversible decision about it.
/// </para>
/// </param>
/// <param name="Reason">
/// Why they are one person. Required, and kept: a merge cannot be undone, and why it was done is the
/// only part of it a reader can still question afterwards.
/// </param>
public sealed record MergeCustomerRequest(
    Guid MergedCustomerId,
    string? MergedCustomerVersion,
    string? Reason)
{
    /// <summary>
    /// Reads the folded record's version as the concrete version it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Deliberately not <c>EntityTag.TryParse</c>.</strong> That parser exists for an
    /// <c>If-Match</c> header, where <c>*</c> is the RFC 9110 wildcard meaning "any current
    /// representation" and honouring it is correct. This field carries the version a manager read off
    /// a screen, and there is no such thing as "any version" of the record they approved destroying.
    /// </para>
    /// <para>
    /// Routing it through the header parser was a real hole rather than a theoretical one: the value
    /// had to be wrapped in quotes to look like a tag, which pushed <c>*</c> past the wildcard branch
    /// and into the quoted-tag branch, where the quotes were stripped again and the result was an
    /// <see cref="EntityTag"/> whose <c>IsAny</c> was true. <c>Matches</c> short-circuits on that, so
    /// a caller could have merged past the precondition by sending one character.
    /// </para>
    /// </remarks>
    /// <param name="version">The parsed version, when the field carries one.</param>
    /// <returns>True when the field is a usable concrete version.</returns>
    public bool TryReadMergedCustomerVersion(out EntityTag version)
    {
        version = default;

        var value = MergedCustomerVersion?.Trim();

        // Empty is the missing field, and a quoted value is somebody sending a header where a version
        // was asked for — the mistake this shape invites, since the value they hold is an ETag.
        if (string.IsNullOrEmpty(value) || value.Contains('"', StringComparison.Ordinal))
        {
            return false;
        }

        var read = new EntityTag(value);

        // And the refusal this method exists for. Neither of the two above would have stopped "*".
        if (read.IsAny)
        {
            return false;
        }

        version = read;

        return true;
    }
}

/// <summary>
/// The receipt for a generated subject-access export. Not the document.
/// </summary>
/// <remarks>
/// It deliberately carries nothing about the person. Everything here describes the artefact — what it
/// is, how big, how long it lasts — so that a screen can show the state of an export, and a log or a
/// support conversation can refer to one, without any of that becoming another place a customer's
/// details are held.
/// </remarks>
/// <param name="ExportId">The export's identity, and the only thing needed to download it.</param>
/// <param name="CustomerId">The person the document is about.</param>
/// <param name="DocumentCode">Which document this is, matching the <c>document.code</c> inside it.</param>
/// <param name="DocumentVersion">The document's shape version.</param>
/// <param name="Classification">
/// The highest data class the document carries, which section 9 requires an export to be marked with.
/// </param>
/// <param name="ContentType">The media type the download is served as.</param>
/// <param name="ByteCount">How large the document is.</param>
/// <param name="GeneratedAt">When it was made.</param>
/// <param name="ExpiresAt">When the download stops working and the copy is destroyed.</param>
/// <param name="SupersededCount">
/// How many earlier copies of this person's data were destroyed to make this one. Almost always zero
/// or one, and worth returning because it is the visible half of the rule that only one copy exists at
/// a time.
/// </param>
public sealed record CustomerExportPayload(
    Guid ExportId,
    Guid CustomerId,
    string DocumentCode,
    int DocumentVersion,
    string Classification,
    string ContentType,
    int ByteCount,
    DateTimeOffset GeneratedAt,
    DateTimeOffset ExpiresAt,
    int SupersededCount)
{
    /// <summary>Projects a receipt.</summary>
    /// <param name="receipt">What the handler generated.</param>
    /// <returns>The payload.</returns>
    public static CustomerExportPayload From(CustomerExportReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new CustomerExportPayload(
            receipt.ExportId,
            receipt.CustomerId,
            receipt.DocumentCode,
            receipt.DocumentVersion,
            receipt.Classification,
            receipt.ContentType,
            receipt.ByteCount,
            receipt.GeneratedAt,
            receipt.ExpiresAt,
            receipt.SupersededCount);
    }
}

/// <summary>What a merge did.</summary>
/// <param name="Customer">The surviving record, with the version a later change is made against.</param>
/// <param name="MergeId">The merge decision, which is what an auditor quotes.</param>
/// <param name="MergedCustomerId">The record that was folded in.</param>
/// <param name="MergedCustomerNumber">
/// The display number that went away. It is now searchable as an alias against the survivor, so
/// somebody holding an old receipt still finds the right person.
/// </param>
/// <param name="AliasesRecorded">
/// One for the merged number, and a second where the two records were written under different names.
/// </param>
/// <param name="VisibilityBranchesAdded">
/// How many branches gained sight of the survivor because they could see the record folded in.
/// </param>
/// <param name="RecordsRepointed">
/// How many records that had already been merged into the folded-in record now name the survivor
/// instead. Usually none.
/// </param>
/// <param name="MergedAt">When the decision was recorded, in UTC.</param>
public sealed record CustomerMergePayload(
    CustomerPayload Customer,
    Guid MergeId,
    Guid MergedCustomerId,
    string MergedCustomerNumber,
    int AliasesRecorded,
    int VisibilityBranchesAdded,
    int RecordsRepointed,
    DateTimeOffset MergedAt)
{
    /// <summary>Projects an outcome, masking the contact fields unless the caller may read them.</summary>
    /// <param name="outcome">What the merge did.</param>
    /// <param name="mayReadContact">Whether the caller holds <c>customers.read_contact</c>.</param>
    /// <returns>The payload.</returns>
    public static CustomerMergePayload From(CustomerMergeOutcome outcome, bool mayReadContact)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new CustomerMergePayload(
            CustomerPayload.From(outcome.Survivor, mayReadContact),
            outcome.MergeId,
            outcome.MergedCustomerId,
            outcome.MergedCustomerNumber,
            outcome.AliasesRecorded,
            outcome.VisibilityBranchesAdded,
            outcome.RecordsRepointed,
            outcome.MergedAt);
    }
}

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
