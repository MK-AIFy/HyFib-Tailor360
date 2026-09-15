using Microsoft.Extensions.Options;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Application.Options;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Orders.Application.Drafts;

/// <summary>
/// Starts and edits an order draft (#199): the counter's own surface over
/// <see cref="OrderDraft"/>, which the aggregate itself already models in full.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is orchestration, not decision-making.</strong> Every rule about what a draft may
/// contain lives on the aggregate; what this class adds is resolving the actor from the authenticated
/// caller, the instant from <see cref="IClock"/>, the identifier from <see cref="IIdGenerator"/>, the
/// window from <see cref="OrdersDraftOptions"/> when the aggregate asks for one, and the one check the
/// aggregate cannot make itself — that the named customer actually exists, which crosses a schema
/// boundary Orders may not read across (ARCH-005) and so is asked of
/// <see cref="ICustomerSnapshotQuery"/> instead.
/// </para>
/// <para>
/// <strong>Garment sections and dependencies are not here.</strong> They are the second half of #199,
/// filed separately: adding, saving and removing a section, and declaring or withdrawing a dependency
/// between two of them, each pull in their own cross-module reads (Catalog's offerability and
/// Customers' measurement existence) that draft lifecycle does not need.
/// </para>
/// </remarks>
/// <param name="store">The draft store.</param>
/// <param name="customers">Customer existence and the merge pointer, never a name or a contact detail.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="options">How long a draft lives when no branch has configured a window of its own.</param>
public sealed class OrderDraftHandler(
    IOrderDraftStore store,
    ICustomerSnapshotQuery customers,
    IAuditWriter audit,
    IClock clock,
    IIdGenerator ids,
    IOptions<OrdersDraftOptions> options)
{
    /// <summary>An order draft was started.</summary>
    /// <remarks>
    /// The documented name from <c>docs/prd/state-transitions.md</c> line 125, underscore-joined rather
    /// than dotted like the two edit actions below it — do not restyle it to match them.
    /// </remarks>
    public const string DraftCreatedAction = "orders.draft_create";

    /// <summary>A draft was pointed at a different customer.</summary>
    public const string CustomerSetAction = "orders.draft.customer_set";

    /// <summary>A draft's order-level schedule was saved.</summary>
    public const string ScheduleSetAction = "orders.draft.schedule_set";

    /// <summary>
    /// Starts a draft, with an order-level schedule if one was given.
    /// </summary>
    /// <remarks>
    /// <c>OrderDraft.Start</c> takes only the customer, deliberately — the schedule is
    /// <c>SetSchedule</c>'s own call. This method folds the two into one so a counter that already knows
    /// the promised date need not make a second request, but the two remain independent domain
    /// operations: a schedule failure here (an over-long note) refuses the whole command rather than
    /// leaving a draft standing with no schedule set.
    /// </remarks>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public async Task<Result<CapturedOrderDraft>> StartAsync(
        StartOrderDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var customerId = await ResolveCustomerAsync(command.CustomerId, cancellationToken);
        if (customerId is null)
        {
            return Result.Failure<CapturedOrderDraft>(OrdersErrors.CustomerNotFound);
        }

        var started = OrderDraft.Start(
            ids.NewId(),
            command.OrganisationId,
            command.BranchId,
            customerId.Value,
            clock.UtcNow,
            options.Value.DraftLifetime,
            command.By);

        if (started.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(started.Error);
        }

        // Start takes only who and where: the order-level schedule is a second domain call, folded into
        // this one command so a caller can supply it up front without the two-step round trip a bare
        // Start-then-SetSchedule would need.
        var scheduled = started.Value.SetSchedule(command.DueDate, command.Notes, clock.UtcNow, command.By);
        if (scheduled.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(scheduled.Error);
        }

        store.Add(started.Value);

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit, DraftCreatedAction, started.Value.Id, "Order draft started.", cancellationToken);

        return Result.Success(new CapturedOrderDraft(started.Value, store.EntityTagOf(started.Value)));
    }

    /// <summary>Reads a draft.</summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be read.</returns>
    public async Task<Result<CapturedOrderDraft>> ReadAsync(
        Guid draftId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var draft = await store.FindAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<CapturedOrderDraft>(OrdersErrors.DraftNotFound);
        }

        return Result.Success(new CapturedOrderDraft(draft, store.EntityTagOf(draft)));
    }

    /// <summary>Points a draft at a different customer.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be saved.</returns>
    public async Task<Result<CapturedOrderDraft>> SetCustomerAsync(
        SetOrderDraftCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadForChangeAsync(
            command.DraftId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(loaded.Error);
        }

        var customerId = await ResolveCustomerAsync(command.CustomerId, cancellationToken);
        if (customerId is null)
        {
            return Result.Failure<CapturedOrderDraft>(OrdersErrors.CustomerNotFound);
        }

        var draft = loaded.Value;

        var changed = draft.SetCustomer(customerId.Value, clock.UtcNow, command.By);
        if (changed.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(changed.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit, CustomerSetAction, draft.Id, "Order draft re-pointed at a different customer.", cancellationToken);

        return Result.Success(new CapturedOrderDraft(draft, store.EntityTagOf(draft)));
    }

    /// <summary>Sets the order-level promised date and notes, replacing both.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason it could not be saved.</returns>
    public async Task<Result<CapturedOrderDraft>> SetScheduleAsync(
        SetOrderDraftScheduleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadForChangeAsync(
            command.DraftId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(loaded.Error);
        }

        var draft = loaded.Value;

        var changed = draft.SetSchedule(command.DueDate, command.Notes, clock.UtcNow, command.By);
        if (changed.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(changed.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit, ScheduleSetAction, draft.Id, "Order draft schedule saved.", cancellationToken);

        return Result.Success(new CapturedOrderDraft(draft, store.EntityTagOf(draft)));
    }

    /// <summary>
    /// Confirms the customer exists and follows the merge pointer, so a draft never stands against a
    /// record that has since been folded into another one.
    /// </summary>
    /// <param name="customerId">The identifier the caller supplied.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier to store — the same one, or the record it was merged into — or null.</returns>
    private async Task<Guid?> ResolveCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        // Permissions are passed through unfiltered — ICustomerSnapshotQuery reads only the one key it
        // documents and ignores the rest, and nothing here needs the contact fields the mask guards.
        var snapshot = await customers.GetAsync(customerId, [], cancellationToken);

        return snapshot?.MergedIntoCustomerId ?? snapshot?.CustomerId;
    }

    private async Task<Result<OrderDraft>> LoadForChangeAsync(
        Guid draftId,
        Guid organisationId,
        EntityTag expected,
        CancellationToken cancellationToken)
    {
        var draft = await store.FindAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<OrderDraft>(OrdersErrors.DraftNotFound);
        }

        // The token belongs to the whole draft, compared before any domain mutation, exactly as
        // DesignSelectionDraftHandler and MeasurementCaptureHandler both do.
        return expected.Matches(store.EntityTagOf(draft))
            ? Result.Success(draft)
            : Result.Failure<OrderDraft>(OrdersErrors.ConcurrentChange);
    }
}
