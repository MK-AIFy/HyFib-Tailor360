using Tailor360.Modules.Customers.Domain.Naming;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Customers.Domain.Customers;

/// <summary>
/// A person the branch serves.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The record is organisation-wide; its visibility is branch-scoped.</strong> That sentence is
/// from <c>docs/prd/workflows/branch-scenarios.md</c> section 3 and it is the shape of this whole
/// aggregate. A customer measured at one branch who walks into another is the same customer, with the
/// same consent and the same measurements — creating a second record because the first was invisible
/// is exception EX-01, and its only remedy is an irreversible merge. So the record is reachable across
/// the organisation, and <see cref="VisibilityBranchIds"/> decides only which branches see it in
/// ordinary search results. Serving the customer at a second branch adds that branch, and the read is
/// audited as a cross-branch read.
/// </para>
/// <para>
/// <strong>Identity never changes.</strong> <see cref="Id"/> and <see cref="CustomerNumber"/> are
/// fixed at creation and are not editable by anything — a correction changes what the record says
/// about the person, never which record it is. The customer number is a display number, allocated
/// from the creating branch's sequence, and never a lookup key on a customer-facing surface
/// (<c>docs/architecture/conventions.md</c> section 3.2).
/// </para>
/// <para>
/// <strong>There is no delete.</strong> See <see cref="CustomerStatus"/>.
/// </para>
/// </remarks>
public sealed class Customer
{
    /// <summary>The longest customer number the column holds.</summary>
    public const int MaximumCustomerNumberLength = 32;

    private readonly List<CustomerAlias> _aliases = [];
    private readonly List<CustomerBranchVisibility> _visibility = [];

    private Customer()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private Customer(
        Guid id,
        Guid organisationId,
        string customerNumber,
        Guid owningBranchId,
        CustomerDetails details,
        DateTimeOffset now,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        CustomerNumber = customerNumber;
        OwningBranchId = owningBranchId;
        _visibility.Add(CustomerBranchVisibility.Add(id, owningBranchId, now, by));
        Status = CustomerStatus.Active;
        CreatedAt = now;
        CreatedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;

        Apply(details);
    }

    /// <summary>Identity of the customer. A UUIDv7, and the only identifier that crosses a boundary.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the record belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>The display number, <c>C-&lt;branch&gt;-000001</c>. Fixed for the life of the record.</summary>
    public string CustomerNumber { get; private set; } = string.Empty;

    /// <summary>
    /// The branch that created the record. It is where the customer number came from and it never
    /// changes; it is not a claim that only that branch may serve the person.
    /// </summary>
    public Guid OwningBranchId { get; private set; }

    /// <summary>
    /// The branches that see this record in ordinary search results.
    /// </summary>
    /// <remarks>
    /// Not an access-control list. A caller who searches across branches is shown a masked
    /// disambiguation card for a record outside their branches, so they can tell whether this is the
    /// same person without opening it; opening it adds their branch here and is audited. The design
    /// and its open question are in <c>docs/prd/workflows/branch-scenarios.md</c> section 3 (BQ-01).
    /// </remarks>
    public IReadOnlyCollection<CustomerBranchVisibility> Visibility => _visibility;

    /// <summary>The branches that see this record, as identifiers.</summary>
    public IEnumerable<Guid> VisibilityBranchIds => _visibility.Select(entry => entry.BranchId);

    /// <summary>The name as the customer gave it. Displayed exactly as entered.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>The folded search key. Never displayed; see the name normaliser.</summary>
    public string NormalisedName { get; private set; } = string.Empty;

    /// <summary>The optional Tamil-script name, searched directly.</summary>
    public string? NativeName { get; private set; }

    /// <summary>The primary telephone number in E.164.</summary>
    public string PhoneE164 { get; private set; } = string.Empty;

    /// <summary>The last six digits of the primary number, which is what the counter searches by.</summary>
    public string PhoneLastSix { get; private set; } = string.Empty;

    /// <summary>A second telephone number in E.164, where one was given.</summary>
    public string? AlternatePhoneE164 { get; private set; }

    /// <summary>The last six digits of the second number.</summary>
    public string? AlternatePhoneLastSix { get; private set; }

    /// <summary>An email address, where one was given.</summary>
    public string? Email { get; private set; }

    /// <summary>The street line of the address.</summary>
    public string? AddressLine { get; private set; }

    /// <summary>The area or town.</summary>
    public string? Locality { get; private set; }

    /// <summary>The postal code.</summary>
    public string? Postcode { get; private set; }

    /// <summary>The language the customer is written to in.</summary>
    public string Language { get; private set; } = CustomerDetails.DefaultLanguage;

    /// <summary>Whether the record is in use.</summary>
    public CustomerStatus Status { get; private set; }

    /// <summary>When the record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who created it, where a person did.</summary>
    public Guid? CreatedBy { get; private set; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last changed it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When it was deactivated, where it has been.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>
    /// The record this one was folded into, or null while it stands on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set once, by <see cref="Absorb"/>, and never cleared: there is no un-merge
    /// (<c>docs/prd/exceptions.md</c> EX-01). A record carrying it is also
    /// <see cref="CustomerStatus.Deactivated"/>, so it drops out of ordinary search — but the row
    /// stays, because an order placed last year still names the person who placed it, and this
    /// pointer is what lets a later reader follow that name to the record that survived.
    /// </para>
    /// <para>
    /// It is deliberately <em>not</em> a fourth customer status. A status is a lifecycle position and
    /// every screen switches on it; a merge is a pointer, and adding a value to the enumeration would
    /// have meant revisiting every place that reads one to decide what it now means.
    /// </para>
    /// </remarks>
    public Guid? MergedIntoCustomerId { get; private set; }

    /// <summary>When this record was folded into another, or null while it stands on its own.</summary>
    public DateTimeOffset? MergedAt { get; private set; }

    /// <summary>True once this record has been folded into another one.</summary>
    public bool IsMerged => MergedIntoCustomerId is not null;

    /// <summary>Previous names, spellings and merged numbers, kept searchable.</summary>
    public IReadOnlyCollection<CustomerAlias> Aliases => _aliases;

    /// <summary>
    /// Creates a customer record.
    /// </summary>
    /// <param name="id">Identity, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="customerNumber">
    /// The allocated display number. Allocation belongs to the application layer, which holds the
    /// sequence and the branch code; the aggregate only checks that it was given one.
    /// </param>
    /// <param name="owningBranchId">The branch the record is created at.</param>
    /// <param name="details">The validated details.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>The customer, or the reason it could not be created.</returns>
    public static Result<Customer> Register(
        Guid id,
        Guid organisationId,
        string? customerNumber,
        Guid owningBranchId,
        CustomerDetails details,
        DateTimeOffset now,
        Guid? by = null)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (id == Guid.Empty)
        {
            return Result.Failure<Customer>(CustomersErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<Customer>(CustomersErrors.Required("organisationId"));
        }

        if (owningBranchId == Guid.Empty)
        {
            return Result.Failure<Customer>(CustomersErrors.NoBranchInContext);
        }

        var number = customerNumber?.Trim();
        if (string.IsNullOrEmpty(number))
        {
            return Result.Failure<Customer>(CustomersErrors.Required("customerNumber"));
        }

        if (number.Length > MaximumCustomerNumberLength)
        {
            return Result.Failure<Customer>(
                CustomersErrors.TooLong("customerNumber", MaximumCustomerNumberLength));
        }

        return Result.Success(
            new Customer(id, organisationId, number, owningBranchId, details, now, by));
    }

    /// <summary>
    /// Corrects what the record says about the person.
    /// </summary>
    /// <remarks>
    /// A correction never changes <see cref="Id"/>, <see cref="CustomerNumber"/> or
    /// <see cref="OwningBranchId"/>. When the name changes, the name it had is kept as an alias, so
    /// that a search for what the customer used to be called still finds them — which is the whole
    /// reason a marriage does not create a second record.
    /// </remarks>
    /// <param name="details">The validated new details.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <param name="aliasId">
    /// The identity to give the alias if one is recorded, from <c>IIdGenerator</c>. Supplied whether
    /// or not it is needed, because the caller cannot know in advance and the domain may not mint one.
    /// </param>
    /// <returns>Success, or the reason the correction was refused.</returns>
    public Result Correct(CustomerDetails details, DateTimeOffset now, Guid? by, Guid aliasId)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (Status is not CustomerStatus.Active)
        {
            return Result.Failure(
                CustomersErrors.StatusTransitionNotAllowed(Status.ToString(), "corrected"));
        }

        if (!string.Equals(DisplayName, details.DisplayName, StringComparison.Ordinal))
        {
            if (aliasId == Guid.Empty)
            {
                return Result.Failure(CustomersErrors.Required("aliasId"));
            }

            _aliases.Add(CustomerAlias.Record(
                aliasId,
                Id,
                CustomerAliasKind.PreviousName,
                DisplayName,
                NormalisedName,
                now,
                by));
        }

        Apply(details);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Withdraws the record from ordinary use.</summary>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Deactivate(DateTimeOffset now, Guid? by)
    {
        if (Status is CustomerStatus.Deactivated)
        {
            return Result.Failure(CustomersErrors.StatusTransitionNotAllowed(
                nameof(CustomerStatus.Deactivated), nameof(CustomerStatus.Deactivated)));
        }

        Status = CustomerStatus.Deactivated;
        DeactivatedAt = now;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Returns the record to ordinary use.</summary>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Reactivate(DateTimeOffset now, Guid? by)
    {
        // Before the status check, because a merged record is deactivated and would otherwise look
        // like an ordinary withdrawal to reverse. Reactivating one would put two records for the same
        // person back in search — the exact state the merge was performed to end.
        if (IsMerged)
        {
            return Result.Failure(CustomersErrors.AlreadyMerged);
        }

        if (Status is CustomerStatus.Active)
        {
            return Result.Failure(CustomersErrors.StatusTransitionNotAllowed(
                nameof(CustomerStatus.Active), nameof(CustomerStatus.Active)));
        }

        Status = CustomerStatus.Active;
        DeactivatedAt = null;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Adds a branch to the record's ordinary visibility, which is what serving the customer there does.
    /// </summary>
    /// <param name="branchId">The branch now serving the customer.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>
    /// True when the branch was added, so the caller knows whether there is anything to audit. False
    /// when the branch could already see the record, which is the ordinary case and not a failure.
    /// </returns>
    public bool MakeVisibleTo(Guid branchId, DateTimeOffset now, Guid? by)
    {
        if (branchId == Guid.Empty || IsVisibleTo(branchId))
        {
            return false;
        }

        _visibility.Add(CustomerBranchVisibility.Add(Id, branchId, now, by));
        Touch(now, by);

        return true;
    }

    /// <summary>
    /// Folds another record for the same person into this one, which survives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the irreversible half of EX-01</strong> and it changes both aggregates: this
    /// one gains the merged record's number as a searchable alias, its display name as a previous
    /// name where the two differ, and every branch that could see it; the merged record gains a
    /// pointer to this one and is withdrawn from ordinary use. Both halves are here rather than in
    /// the application layer, because a merge that applied one and not the other would leave two
    /// records for one person with nothing joining them.
    /// </para>
    /// <para>
    /// <strong>The guards are ordered, and the order is the message.</strong> Self-merge first,
    /// because it is a screen defect and says nothing about either record; then the organisation,
    /// which is a reachability question; then each record's own merge state, survivor before merged,
    /// so that a caller holding two stale records hears "the record you chose to survive has itself
    /// been merged" rather than the vaguer answer about the other one.
    /// </para>
    /// <para>
    /// <strong>What it does not do.</strong> It does not copy the merged record's own aliases, its
    /// consent or its communication preferences. Consent is evidence belonging to the record it was
    /// taken against — <c>docs/nfr/data-classification.md</c> section 5.3 keeps it as written — and
    /// <c>docs/prd/exceptions.md</c> EX-01 says the survivor's own consent and channel preferences
    /// govern every later message. Nor does it re-point measurements or orders: what Customers owns
    /// is re-pointed in the application's transaction, and what it does not own follows
    /// <c>customers.customer-merged.v1</c>.
    /// </para>
    /// </remarks>
    /// <param name="merged">The record being folded in, which this call also changes.</param>
    /// <param name="numberAliasId">Identifier for the alias holding the merged customer number.</param>
    /// <param name="nameAliasId">
    /// Identifier for the alias holding the merged record's display name. Used only when the two
    /// records are written under different names; supplied unconditionally, because a caller cannot
    /// know which case it is in without comparing the two records, which is this method's job.
    /// </param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>What the merge recorded, or the reason it was refused.</returns>
    public Result<MergeAbsorption> Absorb(
        Customer merged,
        Guid numberAliasId,
        Guid nameAliasId,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(merged);

        if (merged.Id == Id)
        {
            return Result.Failure<MergeAbsorption>(CustomersErrors.CannotMergeIntoItself);
        }

        if (merged.OrganisationId != OrganisationId)
        {
            return Result.Failure<MergeAbsorption>(CustomersErrors.CannotMergeAcrossOrganisations);
        }

        if (IsMerged)
        {
            return Result.Failure<MergeAbsorption>(CustomersErrors.MergeTargetIsMerged);
        }

        if (merged.IsMerged)
        {
            return Result.Failure<MergeAbsorption>(CustomersErrors.AlreadyMerged);
        }

        // A withdrawn survivor is refused rather than quietly reactivated. Folding a record into one
        // that has been taken out of use is almost always the two records chosen the wrong way round,
        // and the remedy — reactivate, then merge — is two deliberate steps rather than one guess.
        if (Status is not CustomerStatus.Active)
        {
            return Result.Failure<MergeAbsorption>(
                CustomersErrors.StatusTransitionNotAllowed(Status.ToString(), "a surviving record"));
        }

        if (numberAliasId == Guid.Empty)
        {
            return Result.Failure<MergeAbsorption>(CustomersErrors.Required("numberAliasId"));
        }

        var namesDiffer = !string.Equals(DisplayName, merged.DisplayName, StringComparison.Ordinal);

        if (namesDiffer && nameAliasId == Guid.Empty)
        {
            return Result.Failure<MergeAbsorption>(CustomersErrors.Required("nameAliasId"));
        }

        // The merged number, kept searchable against the record that survived, which is what EX-01
        // promises somebody reading an old receipt.
        _aliases.Add(CustomerAlias.Record(
            numberAliasId,
            Id,
            CustomerAliasKind.MergedCustomerNumber,
            merged.CustomerNumber,
            CustomerNameNormaliser.Normalise(merged.CustomerNumber),
            now,
            by));

        var aliasesRecorded = 1;

        if (namesDiffer)
        {
            _aliases.Add(CustomerAlias.Record(
                nameAliasId,
                Id,
                CustomerAliasKind.PreviousName,
                merged.DisplayName,
                merged.NormalisedName,
                now,
                by));

            aliasesRecorded++;
        }

        // Every branch that could see the record going away must be able to see the one that
        // survives, or the merge would take a customer off a branch's screen altogether.
        var visibilityAdded = 0;

        foreach (var branchId in merged.VisibilityBranchIds.ToList())
        {
            if (MakeVisibleTo(branchId, now, by))
            {
                visibilityAdded++;
            }
        }

        merged.MergeAway(Id, now, by);
        Touch(now, by);

        return Result.Success(new MergeAbsorption(numberAliasId, aliasesRecorded, visibilityAdded));
    }

    /// <summary>
    /// Points this record at the one that absorbed it and withdraws it from ordinary use.
    /// </summary>
    /// <remarks>
    /// Internal, and reachable only through <see cref="Absorb"/>, so the pointer and the alias that
    /// makes the merged number findable are written together or not at all. Idempotent about the
    /// status: a record already withdrawn keeps the date it was withdrawn on, because that is when it
    /// stopped being offered and the merge did not change it.
    /// </remarks>
    /// <param name="survivorCustomerId">The record that survives.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    internal void MergeAway(Guid survivorCustomerId, DateTimeOffset now, Guid? by)
    {
        MergedIntoCustomerId = survivorCustomerId;
        MergedAt = now;

        if (Status is not CustomerStatus.Deactivated)
        {
            Status = CustomerStatus.Deactivated;
            DeactivatedAt = now;
        }

        Touch(now, by);
    }

    /// <summary>Whether a branch sees this record in ordinary search results.</summary>
    /// <param name="branchId">The branch.</param>
    /// <returns>True when it does.</returns>
    public bool IsVisibleTo(Guid branchId)
        => _visibility.Exists(entry => entry.BranchId == branchId);

    private void Apply(CustomerDetails details)
    {
        DisplayName = details.DisplayName;
        NormalisedName = details.NormalisedName;
        NativeName = details.NativeName;
        PhoneE164 = details.Phone.E164;
        PhoneLastSix = details.Phone.LastSix;
        AlternatePhoneE164 = details.AlternatePhone?.E164;
        AlternatePhoneLastSix = details.AlternatePhone?.LastSix;
        Email = details.Email;
        AddressLine = details.AddressLine;
        Locality = details.Locality;
        Postcode = details.Postcode;
        Language = details.Language;
    }

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}

/// <summary>
/// What one merge recorded on the surviving record.
/// </summary>
/// <remarks>
/// Returned by <see cref="Customer.Absorb"/> so that the merge record can say what actually happened
/// rather than what was asked for. The two counts are the difference: two records written under the
/// same name record one alias and not two, and two records already visible to the same branches add
/// no visibility at all. A reader comparing a merge record against the aggregate a year later needs
/// to know which of those it was.
/// </remarks>
/// <param name="NumberAliasId">The alias now holding the merged customer number.</param>
/// <param name="AliasesRecorded">
/// How many aliases the survivor gained: one for the merged number, and a second for the merged
/// record's display name where the two records were written under different names.
/// </param>
/// <param name="VisibilityBranchesAdded">
/// How many branches gained sight of the survivor because they could see the merged record.
/// </param>
public sealed record MergeAbsorption(
    Guid NumberAliasId,
    int AliasesRecorded,
    int VisibilityBranchesAdded);
