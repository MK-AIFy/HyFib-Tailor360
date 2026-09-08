using Tailor360.Modules.Customers.Application.Abstractions;
using Tailor360.Modules.Customers.Contracts.Events;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
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
/// <param name="merges">Where duplicate decisions and merge records are written.</param>
/// <param name="branches">Identity's published branch contract, for the branch code.</param>
/// <param name="events">This module's outbox publisher.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
public sealed class CustomerHandler(
    ICustomerStore customers,
    ICustomerDirectory directory,
    IMergeStore merges,
    IBranchDirectory branches,
    ICustomersEventPublisher events,
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

    /// <summary>A customer record absorbed another and survived.</summary>
    public const string MergedAction = "customers.customer.merged";

    /// <summary>
    /// A customer record was folded into another and no longer stands.
    /// </summary>
    /// <remarks>
    /// A second entry, against the record that went away, rather than one entry naming both. The trail
    /// is read by entity — "what happened to this record" — and a merge is the one change where the
    /// interesting answer for one of the two records is only ever written against the other.
    /// </remarks>
    public const string MergedAwayAction = "customers.customer.merged-away";

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

        // The branch the record is being created in. It is the branch the answer is about: "is this
        // record already on my screen?" is what decides whether the counter opens it or creates a
        // second one, and a card that always said no was the reason EX-01 exists.
        var candidates = await directory.FindDuplicatesAsync(
            command.OrganisationId, subject, [command.BranchId], exceptCustomerId: null, cancellationToken);

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

        // Somebody read the candidates and judged this to be a different person. That judgement is the
        // evidence docs/prd/exceptions.md EX-01 asks for, and it is written in the same save as the
        // record it justifies — evidence for a create that rolled back would be worse than none.
        foreach (var candidate in worthReading)
        {
            var decision = DuplicateCandidateDecision.CreatedNewAnyway(
                ids.NewId(),
                command.OrganisationId,
                customer.Id,
                candidate.Card.CustomerId,
                candidate.Match,
                command.BranchId,
                clock.UtcNow,
                command.By);

            if (decision.IsFailure)
            {
                return Result.Failure<CustomerRegistration>(decision.Error);
            }

            merges.Add(decision.Value);
        }

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

        // A merged record is not opened at a second branch. Adding visibility to it would put a record
        // that no longer stands back in front of a counter, which is the state the merge ended.
        if (customer.IsMerged)
        {
            return Result.Failure<AdministeredCustomer>(CustomersErrors.AlreadyMerged);
        }

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

    /// <summary>
    /// Lists the records that look like they may be the same person as an existing customer.
    /// </summary>
    /// <remarks>
    /// The screen a merge is decided from. It scores the current records rather than reading back the
    /// suspicions raised when either was created, because a resemblance is a fact about the two
    /// records as they stand now — a correction to either can create one or remove it, and a merge is
    /// too final to take on a score somebody computed months ago.
    /// </remarks>
    /// <param name="customerId">The record being examined.</param>
    /// <param name="organisationId">The caller's organisation.</param>
    /// <param name="callerBranchIds">
    /// The branches the caller is assigned to, so a candidate they can already see is not offered as
    /// though it were somebody else's record.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The candidates, strongest first, or the reason the record could not be read.</returns>
    public async Task<Result<IReadOnlyList<DuplicateCandidate>>> DuplicatesAsync(
        Guid customerId,
        Guid organisationId,
        IReadOnlyCollection<Guid> callerBranchIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callerBranchIds);

        var found = await LoadAsync(customerId, organisationId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DuplicateCandidate>>(found.Error);
        }

        var customer = found.Value;

        // A merged record can neither absorb another nor be absorbed again, so every candidate offered
        // for it would be one nobody could act on. An empty list is the honest answer; where the reader
        // should go instead is the record's own merge pointer, which the read endpoint returns.
        if (customer.IsMerged)
        {
            return Result.Success<IReadOnlyList<DuplicateCandidate>>([]);
        }

        return Result.Success(await directory.FindDuplicatesAsync(
            organisationId, SubjectOf(customer), callerBranchIds, customer.Id, cancellationToken));
    }

    /// <summary>
    /// Folds one customer record into another, irreversibly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The remedy for exception EX-01 and the one customer operation that cannot be undone. It is one
    /// transaction over both records, the merge decision, the duplicate decision it settles, any
    /// pointer that named the record going away, and the integration event that tells every other
    /// module to re-point what it holds. Both customer rows are locked for its duration; see
    /// <see cref="ICustomerStore.InMergeTransactionAsync{TOutcome}"/> for why that is a callback.
    /// </para>
    /// <para>
    /// The two audit entries are written afterwards, in the house order — save the change, then record
    /// it — and so is nothing else. There is no compensating path: by the time the entries are
    /// written, the merge has happened.
    /// </para>
    /// </remarks>
    /// <param name="command">Which record survives, which is folded in, and why.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What the merge did, or the reason it was refused.</returns>
    public async Task<Result<CustomerMergeOutcome>> MergeAsync(
        MergeCustomersCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reason = ReadReason(command.Reason);

        if (reason.IsFailure)
        {
            return Result.Failure<CustomerMergeOutcome>(reason.Error);
        }

        // A body that named no record at all. Answered as the missing field it is, rather than opening a
        // transaction, locking the survivor and reporting that the empty identifier was not found.
        if (command.MergedCustomerId == Guid.Empty)
        {
            return Result.Failure<CustomerMergeOutcome>(CustomersErrors.Required("mergedCustomerId"));
        }

        // Refused before a transaction is opened as well, so a screen that sent one record as both
        // sides never takes a row lock and never enters the lock ordering at all.
        if (command.SurvivorCustomerId == command.MergedCustomerId)
        {
            return Result.Failure<CustomerMergeOutcome>(CustomersErrors.CannotMergeIntoItself);
        }

        var committed = await customers.InMergeTransactionAsync(
            command.SurvivorCustomerId,
            command.MergedCustomerId,
            (survivor, merged, token) => ApplyMergeAsync(command, reason.Value, survivor, merged, token),
            cancellationToken);

        if (committed.IsFailure)
        {
            return Result.Failure<CustomerMergeOutcome>(committed.Error);
        }

        var commit = committed.Value;
        var outcome = commit.Outcome;

        await CustomerAudit.RecordAsync(
            audit,
            MergedAction,
            outcome.Survivor.CustomerId,
            $"Absorbed customer record {outcome.MergedCustomerNumber}. "
            + $"Aliases recorded: {outcome.AliasesRecorded}. "
            + $"Branches that gained sight of this record: {outcome.VisibilityBranchesAdded}. "
            + $"Earlier merges re-pointed here: {outcome.RecordsRepointed}.",
            reason.Value,
            commit.SurvivorBefore,
            commit.SurvivorAfter,
            cancellationToken);

        await CustomerAudit.RecordAsync(
            audit,
            MergedAwayAction,
            outcome.MergedCustomerId,
            "Folded into another customer record and withdrawn from ordinary use. The number stays "
            + "searchable against the record that survived, and there is no un-merge.",
            reason.Value,
            commit.MergedBefore,
            commit.MergedAfter,
            cancellationToken);

        return Result.Success(outcome);
    }

    private async Task<Result<MergeCommit>> ApplyMergeAsync(
        MergeCustomersCommand command,
        string reason,
        Customer survivor,
        Customer merged,
        CancellationToken cancellationToken)
    {
        // "Not there" and "not yours" are one answer here as everywhere else in this handler.
        if (survivor.OrganisationId != command.OrganisationId
            || merged.OrganisationId != command.OrganisationId)
        {
            return Result.Failure<MergeCommit>(CustomersErrors.CustomerNotFound);
        }

        // The caller's If-Match was checked against a read taken before the row was locked. Checking it
        // again here, against the row this transaction will actually change, is what makes the
        // precondition mean anything: in between, somebody could have corrected the record the caller
        // read and approved.
        if (!command.ExpectedVersion.Matches(customers.EntityTagOf(survivor)))
        {
            return Result.Failure<MergeCommit>(CustomersErrors.ConcurrentChange);
        }

        // Scored before the absorption, so the stored decision explains the pair a person was looking
        // at rather than the single record left afterwards.
        var match = DuplicateScoring.Compare(SubjectOf(survivor), SubjectOf(merged));

        var survivorBefore = CustomerSnapshot.Of(survivor);
        var mergedBefore = CustomerSnapshot.Of(merged);

        var now = clock.UtcNow;
        var absorbed = survivor.Absorb(merged, ids, now, command.By);

        if (absorbed.IsFailure)
        {
            return Result.Failure<MergeCommit>(absorbed.Error);
        }

        var mergeId = ids.NewId();
        var eventId = ids.NewId();

        var record = CustomerMerge.Record(
            mergeId,
            command.OrganisationId,
            survivor,
            merged,
            reason,
            command.BranchId,
            absorbed.Value,
            eventId,
            now,
            command.By);

        if (record.IsFailure)
        {
            return Result.Failure<MergeCommit>(record.Error);
        }

        merges.Add(record.Value);

        var decision = DuplicateCandidateDecision.Merged(
            ids.NewId(),
            command.OrganisationId,
            survivor.Id,
            merged.Id,
            match,
            mergeId,
            command.BranchId,
            now,
            command.By);

        if (decision.IsFailure)
        {
            return Result.Failure<MergeCommit>(decision.Error);
        }

        merges.Add(decision.Value);

        var repointed = await customers.FlattenMergePointersAsync(
            merged.Id, survivor.Id, now, command.By, cancellationToken);

        // Published before the save, so the message and the merge are one transaction on one
        // connection (#77). A merge committed without the message would leave every other module
        // pointing at a record that no longer stands, with nothing to tell it so.
        events.Publish(new CustomerMerged(
            eventId,
            now,
            survivor.Id,
            command.OrganisationId,
            merged.Id,
            mergeId,
            command.BranchId));

        var saved = await customers.TrySaveChangesAsync(cancellationToken);

        if (saved.IsFailure)
        {
            return Result.Failure<MergeCommit>(saved.Error);
        }

        return Result.Success(new MergeCommit(
            new CustomerMergeOutcome(
                AdministeredCustomer.From(survivor, customers.EntityTagOf(survivor)),
                mergeId,
                merged.Id,
                record.Value.MergedCustomerNumber,
                absorbed.Value.AliasesRecorded,
                absorbed.Value.VisibilityBranchesAdded,
                repointed,
                now),
            survivorBefore,
            CustomerSnapshot.Of(survivor, mergedWith: merged.Id),
            mergedBefore,
            CustomerSnapshot.Of(merged, mergedWith: survivor.Id)));
    }

    /// <summary>
    /// Everything one merge produced, held together until the trail can be written.
    /// </summary>
    /// <remarks>
    /// The four snapshots have to be taken inside the merge transaction, while both aggregates are
    /// loaded and locked, but the entries themselves are written afterwards — the audit writer has its
    /// own context, and the house order is to save the change first and record it second. This carries
    /// them across that boundary.
    /// </remarks>
    private sealed record MergeCommit(
        CustomerMergeOutcome Outcome,
        CustomerSnapshot SurvivorBefore,
        CustomerSnapshot SurvivorAfter,
        CustomerSnapshot MergedBefore,
        CustomerSnapshot MergedAfter);

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
    /// The same subject, read off a record that already exists.
    /// </summary>
    /// <remarks>
    /// It takes the <em>stored</em> normalised name rather than folding the display name again, so a
    /// score is computed against the key the index is built on. Re-folding here would be a second
    /// implementation of the same rule, and the two would disagree the first time the normaliser
    /// changed — silently, and in favour of the copy nobody indexed.
    /// </remarks>
    private static DuplicateSubject SubjectOf(Customer customer) => new(
        customer.NormalisedName,
        customer.NativeName,
        customer.PhoneE164,
        customer.AlternatePhoneE164,
        customer.Locality,
        customer.Postcode);

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

/// <summary>
/// The decision that two customer records are one person.
/// </summary>
/// <remarks>
/// <see cref="ExpectedVersion"/> is carried in the command rather than checked only at the edge, so
/// that the precondition is re-tested against the row the merge locks. An <c>If-Match</c> compared
/// against a read taken before the lock proves the caller saw <em>a</em> version, not the version
/// about to change.
/// </remarks>
/// <param name="SurvivorCustomerId">The record that is to survive.</param>
/// <param name="MergedCustomerId">The record that is to be folded in.</param>
/// <param name="OrganisationId">The caller's organisation. Both records must belong to it.</param>
/// <param name="BranchId">The branch the decision is being taken at, where there is one.</param>
/// <param name="ExpectedVersion">The version of the surviving record the caller read.</param>
/// <param name="Reason">Why they are one person. Required; a merge cannot be undone.</param>
/// <param name="By">The actor.</param>
public sealed record MergeCustomersCommand(
    Guid SurvivorCustomerId,
    Guid MergedCustomerId,
    Guid OrganisationId,
    Guid? BranchId,
    EntityTag ExpectedVersion,
    string? Reason,
    Guid? By);

/// <summary>What one merge did.</summary>
/// <param name="Survivor">The surviving record, with the version any later change is made against.</param>
/// <param name="MergeId">The merge decision, which is what an auditor quotes.</param>
/// <param name="MergedCustomerId">The record that was folded in.</param>
/// <param name="MergedCustomerNumber">
/// The display number that went away, which is now searchable as an alias on the survivor.
/// </param>
/// <param name="AliasesRecorded">
/// One for the merged number, and a second where the two records were written under different names.
/// </param>
/// <param name="VisibilityBranchesAdded">
/// How many branches gained sight of the survivor because they could see the record folded in.
/// </param>
/// <param name="RecordsRepointed">
/// How many records that had already been merged into the folded-in record now name the survivor
/// instead. Usually none; it is not none when a merge is being corrected by a second merge.
/// </param>
/// <param name="MergedAt">When the decision was recorded, in UTC.</param>
public sealed record CustomerMergeOutcome(
    AdministeredCustomer Survivor,
    Guid MergeId,
    Guid MergedCustomerId,
    string MergedCustomerNumber,
    int AliasesRecorded,
    int VisibilityBranchesAdded,
    int RecordsRepointed,
    DateTimeOffset MergedAt);
