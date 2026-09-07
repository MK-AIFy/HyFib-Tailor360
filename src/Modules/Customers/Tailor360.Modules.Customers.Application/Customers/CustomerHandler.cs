using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Customers.Application.Customers;

/// <summary>
/// Everything the customer record does.
/// </summary>
/// <remarks>
/// <para>
/// The handler owns the sequence of steps and the audit entry; the aggregate owns the invariants and
/// the directory owns the queries. It takes the caller's organisation and branch as parameters rather
/// than reading a security context, so that every path through it can be exercised without a host.
/// </para>
/// <para>
/// <strong>Reach is not decided here.</strong> A customer record is organisation-wide
/// (<c>docs/prd/workflows/branch-scenarios.md</c> section 3.2), so the handler checks that a record
/// belongs to the caller's <em>organisation</em> and nothing narrower. Branch visibility decides what
/// a search offers, which is the directory's job, and permission decides who may ask at all, which is
/// the endpoint's.
/// </para>
/// </remarks>
/// <param name="customers">The record store.</param>
/// <param name="directory">The search and duplicate queries.</param>
/// <param name="branches">Identity's published branch contract, for the branch code.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class CustomerHandler(
    ICustomerStore customers,
    ICustomerDirectory directory,
    IBranchDirectory branches,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids)
{
    /// <summary>A customer record was created.</summary>
    public const string RegisteredAction = "customers.customer.registered";

    /// <summary>A customer record was corrected.</summary>
    public const string CorrectedAction = "customers.customer.corrected";

    /// <summary>A customer record was withdrawn from ordinary use.</summary>
    public const string DeactivatedAction = "customers.customer.deactivated";

    /// <summary>A customer record was returned to ordinary use.</summary>
    public const string ReactivatedAction = "customers.customer.reactivated";

    /// <summary>A branch other than the owning one began serving the customer.</summary>
    public const string OpenedAtBranchAction = "customers.customer.opened-at-branch";

    /// <summary>The shortest reason the trail accepts.</summary>
    public const int MinimumReasonLength = 3;

    /// <summary>The longest reason the trail accepts.</summary>
    public const int MaximumReasonLength = 500;

    /// <summary>The sequence customer numbers are allocated from.</summary>
    public const string CustomerNumberSequence = "customer";

    /// <summary>
    /// Creates a customer record, unless it looks enough like one that already exists that somebody
    /// should read the candidates first.
    /// </summary>
    /// <param name="command">What to create and where.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The registration. When <see cref="CustomerRegistration.Customer"/> is null the record was not
    /// created and <see cref="CustomerRegistration.Candidates"/> says what to read first.
    /// </returns>
    public async Task<Result<CustomerRegistration>> RegisterAsync(
        RegisterCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.BranchId == Guid.Empty)
        {
            return Result.Failure<CustomerRegistration>(CustomersErrors.NoBranchInContext);
        }

        var branch = await branches.FindAsync(command.BranchId, cancellationToken);
        if (branch is null || !branch.IsOpen)
        {
            return Result.Failure<CustomerRegistration>(CustomersErrors.NoBranchInContext);
        }

        var subject = Subject(command.Details);
        var candidates = await directory.FindDuplicatesAsync(
            command.OrganisationId, subject, cancellationToken);

        // Only a candidate somebody would want to read stops the create. A weak resemblance is shown
        // on the screen beside the form and never blocks: a warning that fires on every common name
        // is a warning the counter learns to click through.
        var worthReading = candidates
            .Where(candidate => candidate.Match.Confidence >= DuplicateConfidence.Medium)
            .ToList();

        if (worthReading.Count > 0 && !command.DuplicatesReviewed)
        {
            return Result.Success(new CustomerRegistration(null, worthReading));
        }

        var number = await customers.NextCustomerNumberAsync(branch.Code, cancellationToken);

        var registered = Customer.Register(
            ids.NewId(),
            command.OrganisationId,
            number,
            command.BranchId,
            command.Details,
            clock.UtcNow,
            command.By);

        if (registered.IsFailure)
        {
            return Result.Failure<CustomerRegistration>(registered.Error);
        }

        var customer = registered.Value;
        customers.Add(customer);

        var saved = await customers.TrySaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<CustomerRegistration>(saved.Error);
        }

        await CustomerAudit.RecordAsync(
            audit,
            RegisteredAction,
            customer.Id,
            $"Customer record created at branch {branch.Code}.",
            command.Reason,
            before: null,
            after: CustomerSnapshot.Of(customer),
            cancellationToken);

        return Result.Success(new CustomerRegistration(
            AdministeredCustomer.From(customer, customers.EntityTagOf(customer)),
            []));
    }

    /// <summary>Reads one customer record.</summary>
    /// <param name="customerId">The record.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or the reason it could not be read.</returns>
    public async Task<Result<AdministeredCustomer>> ReadAsync(
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var found = await LoadAsync(customerId, organisationId, cancellationToken);

        return found.IsFailure
            ? Result.Failure<AdministeredCustomer>(found.Error)
            : Result.Success(AdministeredCustomer.From(found.Value, customers.EntityTagOf(found.Value)));
    }

    /// <summary>Corrects what a record says about the person.</summary>
    /// <param name="command">The correction.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The corrected record, or the reason it was refused.</returns>
    public async Task<Result<AdministeredCustomer>> CorrectAsync(
        CorrectCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reason = ReadReason(command.Reason);
        if (reason.IsFailure)
        {
            return Result.Failure<AdministeredCustomer>(reason.Error);
        }

        var found = await LoadAsync(command.CustomerId, command.OrganisationId, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredCustomer>(found.Error);
        }

        var customer = found.Value;
        var before = CustomerSnapshot.Of(customer);
        var changed = ChangedFields(customer, command.Details);

        var corrected = customer.Correct(command.Details, clock.UtcNow, command.By, ids.NewId());
        if (corrected.IsFailure)
        {
            return Result.Failure<AdministeredCustomer>(corrected.Error);
        }

        return await CommitAsync(
            customer,
            CorrectedAction,
            $"Customer record corrected: {(changed.Count == 0 ? "no field changed" : string.Join(", ", changed))}.",
            reason.Value,
            before,
            CustomerSnapshot.Of(customer, changed),
            cancellationToken);
    }

    /// <summary>Withdraws a record from ordinary use.</summary>
    /// <param name="customerId">The record.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="reason">Why.</param>
    /// <param name="by">The actor.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The record, or the reason it was refused.</returns>
    public Task<Result<AdministeredCustomer>> DeactivateAsync(
        Guid customerId,
        Guid organisationId,
        string? reason,
        Guid? by,
        CancellationToken cancellationToken = default)
        => ChangeStatusAsync(
            customerId,
            organisationId,
            reason,
            by,
            DeactivatedAction,
            "Customer record withdrawn from ordinary use.",
            (customer, now, actor) => customer.Deactivate(now, actor),
            cancellationToken);

    /// <summary>Returns a record to ordinary use.</summary>
    /// <param name="customerId">The record.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="reason">Why.</param>
    /// <param name="by">The actor.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The record, or the reason it was refused.</returns>
    public Task<Result<AdministeredCustomer>> ReactivateAsync(
        Guid customerId,
        Guid organisationId,
        string? reason,
        Guid? by,
        CancellationToken cancellationToken = default)
        => ChangeStatusAsync(
            customerId,
            organisationId,
            reason,
            by,
            ReactivatedAction,
            "Customer record returned to ordinary use.",
            (customer, now, actor) => customer.Reactivate(now, actor),
            cancellationToken);

    /// <summary>
    /// Records that a branch has begun serving this customer, which is what opening the record does.
    /// </summary>
    /// <remarks>
    /// A separate, audited command rather than a side effect of the read. Reading a record must not
    /// change it, and this change is exactly the cross-branch event
    /// <c>branch-scenarios.md</c> section 3.1 says is recorded with actor, branch and correlation.
    /// Opening a record the branch already sees changes nothing and writes no entry.
    /// </remarks>
    /// <param name="customerId">The record.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="branchId">The branch now serving the customer.</param>
    /// <param name="by">The actor.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The record, or the reason it was refused.</returns>
    public async Task<Result<AdministeredCustomer>> OpenAtBranchAsync(
        Guid customerId,
        Guid organisationId,
        Guid branchId,
        Guid? by,
        CancellationToken cancellationToken = default)
    {
        if (branchId == Guid.Empty)
        {
            return Result.Failure<AdministeredCustomer>(CustomersErrors.NoBranchInContext);
        }

        var found = await LoadAsync(customerId, organisationId, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredCustomer>(found.Error);
        }

        var customer = found.Value;
        var before = CustomerSnapshot.Of(customer);

        if (!customer.MakeVisibleTo(branchId, clock.UtcNow, by))
        {
            return Result.Success(AdministeredCustomer.From(customer, customers.EntityTagOf(customer)));
        }

        return await CommitAsync(
            customer,
            OpenedAtBranchAction,
            "A second branch began serving this customer and now sees the record in search.",
            reason: null,
            before,
            CustomerSnapshot.Of(customer),
            cancellationToken);
    }

    /// <summary>Runs a counter search.</summary>
    /// <param name="query">What to look for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One page of cards, or the reason the search was refused.</returns>
    public async Task<Result<CustomerSearchPage>> SearchAsync(
        CustomerSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var term = query.Term?.Trim();

        // A search with nothing to search on would return the organisation's whole customer list to
        // anybody holding customers.read. An empty result is the honest answer to an empty question.
        if (term is null || term.Length < CustomerSearchQuery.MinimumTermLength)
        {
            return Result.Success(new CustomerSearchPage([], null));
        }

        var bounded = query with
        {
            Term = term,
            Limit = Math.Clamp(query.Limit, 1, CustomerSearchQuery.MaximumLimit),
        };

        return Result.Success(await directory.SearchAsync(bounded, cancellationToken));
    }

    private async Task<Result<Customer>> LoadAsync(
        Guid customerId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var customer = await customers.FindAsync(customerId, cancellationToken);

        // "Not there" and "not yours" are one answer. Telling the two apart is the identifier-editing
        // oracle the authorisation design closes, and the organisation is the only boundary that
        // applies: within it, the record is reachable by design.
        return customer is null || customer.OrganisationId != organisationId
            ? Result.Failure<Customer>(CustomersErrors.CustomerNotFound)
            : Result.Success(customer);
    }

    private async Task<Result<AdministeredCustomer>> ChangeStatusAsync(
        Guid customerId,
        Guid organisationId,
        string? reason,
        Guid? by,
        string action,
        string summary,
        Func<Customer, DateTimeOffset, Guid?, Result> change,
        CancellationToken cancellationToken)
    {
        var read = ReadReason(reason);
        if (read.IsFailure)
        {
            return Result.Failure<AdministeredCustomer>(read.Error);
        }

        var found = await LoadAsync(customerId, organisationId, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<AdministeredCustomer>(found.Error);
        }

        var customer = found.Value;
        var before = CustomerSnapshot.Of(customer);

        var changed = change(customer, clock.UtcNow, by);
        if (changed.IsFailure)
        {
            return Result.Failure<AdministeredCustomer>(changed.Error);
        }

        return await CommitAsync(
            customer, action, summary, read.Value, before, CustomerSnapshot.Of(customer), cancellationToken);
    }

    private async Task<Result<AdministeredCustomer>> CommitAsync(
        Customer customer,
        string action,
        string summary,
        string? reason,
        CustomerSnapshot before,
        CustomerSnapshot after,
        CancellationToken cancellationToken)
    {
        var saved = await customers.TrySaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredCustomer>(saved.Error);
        }

        await CustomerAudit.RecordAsync(
            audit, action, customer.Id, summary, reason, before, after, cancellationToken);

        return Result.Success(AdministeredCustomer.From(customer, customers.EntityTagOf(customer)));
    }

    private static Result<string> ReadReason(string? reason)
    {
        var trimmed = reason?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Failure<string>(CustomersErrors.ReasonRequired);
        }

        return trimmed.Length is < MinimumReasonLength or > MaximumReasonLength
            ? Result.Failure<string>(
                CustomersErrors.ReasonOutOfBounds(MinimumReasonLength, MaximumReasonLength))
            : Result.Success(trimmed);
    }

    private static DuplicateSubject Subject(CustomerDetails details) => new(
        details.NormalisedName,
        details.NativeName,
        details.Phone.E164,
        details.AlternatePhone?.E164,
        details.Locality,
        details.Postcode);

    /// <summary>
    /// Which fields a correction touches, by name. Names only — the trail says what changed and never
    /// what it changed to.
    /// </summary>
    private static List<string> ChangedFields(Customer customer, CustomerDetails details)
    {
        var changed = new List<string>();

        Compare("displayName", customer.DisplayName, details.DisplayName);
        Compare("nativeName", customer.NativeName, details.NativeName);
        Compare("phone", customer.PhoneE164, details.Phone.E164);
        Compare("alternatePhone", customer.AlternatePhoneE164, details.AlternatePhone?.E164);
        Compare("email", customer.Email, details.Email);
        Compare("addressLine", customer.AddressLine, details.AddressLine);
        Compare("locality", customer.Locality, details.Locality);
        Compare("postcode", customer.Postcode, details.Postcode);
        Compare("language", customer.Language, details.Language);

        return changed;

        void Compare(string field, string? was, string? now)
        {
            if (!string.Equals(was, now, StringComparison.Ordinal))
            {
                changed.Add(field);
            }
        }
    }
}

/// <summary>What to create, and where.</summary>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="BranchId">The branch the record is created at, which allocates its number.</param>
/// <param name="Details">The validated details.</param>
/// <param name="DuplicatesReviewed">
/// True when the caller has read the candidates and says this is somebody new. The decision is the
/// person's and the trail records that they took it.
/// </param>
/// <param name="Reason">An optional note, recorded with the entry.</param>
/// <param name="By">The actor.</param>
public sealed record RegisterCustomerCommand(
    Guid OrganisationId,
    Guid BranchId,
    CustomerDetails Details,
    bool DuplicatesReviewed,
    string? Reason,
    Guid? By);

/// <summary>What to correct.</summary>
/// <param name="CustomerId">The record.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="Details">The validated new details.</param>
/// <param name="Reason">Why. Required: a correction with no reason is a mystery in the timeline.</param>
/// <param name="By">The actor.</param>
public sealed record CorrectCustomerCommand(
    Guid CustomerId,
    Guid OrganisationId,
    CustomerDetails Details,
    string? Reason,
    Guid? By);

/// <summary>The outcome of an attempt to create a record.</summary>
/// <param name="Customer">The record, or null when candidates must be read first.</param>
/// <param name="Candidates">
/// What to read first, strongest first. Empty when the record was created.
/// </param>
public sealed record CustomerRegistration(
    AdministeredCustomer? Customer,
    IReadOnlyList<DuplicateCandidate> Candidates);
