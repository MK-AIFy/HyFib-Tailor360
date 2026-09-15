using Microsoft.Extensions.Options;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Modules.Customers.Contracts.Measurements;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Application.Options;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Modules.Orders.Domain.Jobs;
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
/// <strong>Garment sections pin their catalogue and measurement references server-side.</strong>
/// <c>OrderDraftGarmentContent.Create</c> checks only that a category and service key are non-empty and
/// short enough, and that the measurement pair is internally consistent — never that either is real. A
/// section's <c>catalogVersionId</c> and <c>measurementTemplateId</c> come from
/// <see cref="ICatalogAvailabilityQuery.GetOrderableCatalogAsync"/>, the same pin a confirmed order is
/// frozen against, and a reused measurement version is confirmed to exist through
/// <see cref="IMeasurementSnapshotQuery.ExistingAsync"/> — never <c>GetAsync</c>, whose values are
/// sensitive personal data a draft must not hold.
/// </para>
/// </remarks>
/// <param name="store">The draft store.</param>
/// <param name="customers">Customer existence and the merge pointer, never a name or a contact detail.</param>
/// <param name="catalog">What a branch may order today, for the pin a garment section takes.</param>
/// <param name="measurements">Whether a reused measurement version exists, never its values.</param>
/// <param name="audit">The platform's audit writer.</param>
/// <param name="clock">The clock.</param>
/// <param name="ids">The identifier generator.</param>
/// <param name="options">How long a draft lives when no branch has configured a window of its own.</param>
public sealed class OrderDraftHandler(
    IOrderDraftStore store,
    ICustomerSnapshotQuery customers,
    ICatalogAvailabilityQuery catalog,
    IMeasurementSnapshotQuery measurements,
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

    /// <summary>A garment section was added to a draft.</summary>
    public const string GarmentAddedAction = "orders.draft.garment_added";

    /// <summary>A garment section's content was replaced.</summary>
    public const string GarmentSavedAction = "orders.draft.garment_saved";

    /// <summary>A garment section was removed from a draft.</summary>
    public const string GarmentRemovedAction = "orders.draft.garment_removed";

    /// <summary>A dependency between two garment sections was declared.</summary>
    public const string DependencyDeclaredAction = "orders.draft.dependency_declared";

    /// <summary>A dependency between two garment sections was withdrawn.</summary>
    public const string DependencyWithdrawnAction = "orders.draft.dependency_withdrawn";

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

    /// <summary>
    /// Reads one garment section, for the current tag a stale <c>If-Match</c> is answered with.
    /// </summary>
    /// <param name="draftId">The draft.</param>
    /// <param name="garmentId">The section.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The section, or the reason it could not be read.</returns>
    public async Task<Result<CapturedOrderDraftGarment>> ReadGarmentAsync(
        Guid draftId,
        Guid garmentId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var draft = await store.FindAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<CapturedOrderDraftGarment>(OrdersErrors.DraftNotFound);
        }

        if (draft.FindGarment(garmentId) is not { } garment)
        {
            return Result.Failure<CapturedOrderDraftGarment>(OrdersErrors.GarmentNotOnDraft);
        }

        return Result.Success(new CapturedOrderDraftGarment(draft, garment, store.EntityTagOf(garment)));
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

    /// <summary>Adds a garment section to a draft.</summary>
    /// <remarks>
    /// No <c>If-Match</c>: this creates the section, so there is no earlier version to be stale against.
    /// The position race two counters can cause by adding at the same instant is settled by the unique
    /// index over <c>(order_draft_id, position)</c> and surfaces as <see cref="OrdersErrors.ConcurrentChange"/>
    /// through <see cref="IOrderDraftStore.SaveAsync"/> — the loser re-reads and retries, and no loop is
    /// added here for that.
    /// </remarks>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The section, or the reason it could not be added.</returns>
    public async Task<Result<CapturedOrderDraftGarment>> AddGarmentAsync(
        AddOrderDraftGarmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var draft = await store.FindAsync(command.DraftId, command.OrganisationId, cancellationToken);
        if (draft is null)
        {
            return Result.Failure<CapturedOrderDraftGarment>(OrdersErrors.DraftNotFound);
        }

        var content = await ResolveGarmentContentAsync(
            draft.OrganisationId,
            draft.BranchId,
            command.CategoryKey,
            command.ServiceTypeKey,
            command.DesignSelectionDraftId,
            command.MeasurementIntent,
            command.MeasurementVersionId,
            command.DueDate,
            command.Instructions,
            command.ReferenceMediaIds,
            cancellationToken);

        if (content.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(content.Error);
        }

        var added = draft.AddGarment(ids.NewId(), content.Value, clock.UtcNow, command.By);
        if (added.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(added.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit, GarmentAddedAction, added.Value.Id, "Garment section added to an order draft.", cancellationToken);

        return Result.Success(
            new CapturedOrderDraftGarment(draft, added.Value, store.EntityTagOf(added.Value)));
    }

    /// <summary>Replaces the whole content of a garment section.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The section, or the reason it could not be saved.</returns>
    public async Task<Result<CapturedOrderDraftGarment>> SaveGarmentAsync(
        SaveOrderDraftGarmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadGarmentForChangeAsync(
            command.DraftId, command.GarmentId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(loaded.Error);
        }

        var (draft, garment) = loaded.Value;

        var content = await ResolveGarmentContentAsync(
            draft.OrganisationId,
            draft.BranchId,
            command.CategoryKey,
            command.ServiceTypeKey,
            command.DesignSelectionDraftId,
            command.MeasurementIntent,
            command.MeasurementVersionId,
            command.DueDate,
            command.Instructions,
            command.ReferenceMediaIds,
            cancellationToken);

        if (content.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(content.Error);
        }

        var saveOutcome = draft.SaveGarment(garment.Id, content.Value, clock.UtcNow, command.By);
        if (saveOutcome.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(saveOutcome.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit, GarmentSavedAction, garment.Id, "Garment section content replaced.", cancellationToken);

        return Result.Success(new CapturedOrderDraftGarment(draft, garment, store.EntityTagOf(garment)));
    }

    /// <summary>Removes a garment section, and every dependency naming it in either direction.</summary>
    /// <remarks>
    /// Answers with the whole draft, not the removed section: the removal can withdraw dependencies on
    /// other sections too, so a caller re-reading only the section it named would miss what else changed.
    /// </remarks>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The draft, or the reason the removal was refused.</returns>
    public async Task<Result<CapturedOrderDraft>> RemoveGarmentAsync(
        RemoveOrderDraftGarmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadGarmentForChangeAsync(
            command.DraftId, command.GarmentId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(loaded.Error);
        }

        var (draft, garment) = loaded.Value;

        var removed = draft.RemoveGarment(garment.Id, clock.UtcNow, command.By);
        if (removed.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(removed.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<CapturedOrderDraft>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit, GarmentRemovedAction, draft.Id, "Garment section removed from an order draft.", cancellationToken);

        return Result.Success(new CapturedOrderDraft(draft, store.EntityTagOf(draft)));
    }

    /// <summary>Declares that one section waits for, or is delivered with, another section of the same draft.</summary>
    /// <remarks>
    /// The precondition is the dependent section's own tag. Declaring touches the garment
    /// (<c>OrderDraftGarment.DeclareDependency</c> calls <c>Touch</c>), so the section's row moves and this
    /// is a genuine token even though the dependency table itself carries none.
    /// </remarks>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dependent section, or the reason the declaration was refused.</returns>
    public async Task<Result<CapturedOrderDraftGarment>> DeclareDependencyAsync(
        DeclareOrderDraftDependencyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadGarmentForChangeAsync(
            command.DraftId, command.GarmentId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(loaded.Error);
        }

        var (draft, garment) = loaded.Value;

        var declared = draft.DeclareDependency(
            garment.Id, command.PrerequisiteGarmentId, command.Kind, command.Reason, clock.UtcNow, command.By);
        if (declared.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(declared.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit, DependencyDeclaredAction, garment.Id, "Garment dependency declared.", cancellationToken);

        return Result.Success(new CapturedOrderDraftGarment(draft, garment, store.EntityTagOf(garment)));
    }

    /// <summary>Withdraws a dependency one section declared on another.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dependent section, or the reason the withdrawal was refused.</returns>
    public async Task<Result<CapturedOrderDraftGarment>> WithdrawDependencyAsync(
        WithdrawOrderDraftDependencyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await LoadGarmentForChangeAsync(
            command.DraftId, command.GarmentId, command.OrganisationId, command.ExpectedVersion, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(loaded.Error);
        }

        var (draft, garment) = loaded.Value;

        var withdrawn = draft.WithdrawDependency(
            garment.Id, command.PrerequisiteGarmentId, command.Kind, clock.UtcNow, command.By);
        if (withdrawn.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(withdrawn.Error);
        }

        var saved = await store.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<CapturedOrderDraftGarment>(saved.Error);
        }

        await OrdersAudit.RecordAsync(
            audit, DependencyWithdrawnAction, garment.Id, "Garment dependency withdrawn.", cancellationToken);

        return Result.Success(new CapturedOrderDraftGarment(draft, garment, store.EntityTagOf(garment)));
    }

    /// <summary>
    /// Pins a garment section's catalogue and measurement references server-side, and validates the rest
    /// through <see cref="OrderDraftGarmentContent.Create"/>.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="AddGarmentAsync"/> and <see cref="SaveGarmentAsync"/>, which take identical
    /// content and differ only in what they do with it once validated.
    /// </remarks>
    private async Task<Result<OrderDraftGarmentContent>> ResolveGarmentContentAsync(
        Guid organisationId,
        Guid branchId,
        string? categoryKey,
        string? serviceTypeKey,
        Guid? designSelectionDraftId,
        MeasurementIntent measurementIntent,
        Guid? measurementVersionId,
        DateOnly? dueDate,
        string? instructions,
        IReadOnlyCollection<Guid>? referenceMediaIds,
        CancellationToken cancellationToken)
    {
        var offered = await catalog.GetOrderableCatalogAsync(
            organisationId, branchId, clock.UtcNow, cancellationToken);

        var service = offered.Services.FirstOrDefault(candidate =>
            string.Equals(candidate.CategoryCode, categoryKey, StringComparison.Ordinal)
            && string.Equals(candidate.ServiceCode, serviceTypeKey, StringComparison.Ordinal));

        if (service is null)
        {
            return Result.Failure<OrderDraftGarmentContent>(OrdersErrors.ServiceNotOrderableHere);
        }

        if (measurementIntent is MeasurementIntent.ReuseVersion && measurementVersionId is { } reused)
        {
            var existing = await measurements.ExistingAsync([reused], organisationId, cancellationToken);
            if (!existing.Contains(reused))
            {
                return Result.Failure<OrderDraftGarmentContent>(OrdersErrors.MeasurementVersionNotFound);
            }
        }

        return OrderDraftGarmentContent.Create(
            categoryKey,
            serviceTypeKey,
            service.CatalogVersionId,
            designSelectionDraftId,
            measurementIntent,
            measurementVersionId,
            service.MeasurementTemplateId,
            dueDate,
            instructions,
            referenceMediaIds);
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

    /// <summary>
    /// Loads a draft and one of its garment sections, comparing the precondition against the
    /// <strong>section's own</strong> tag rather than the draft's — the per-garment lock
    /// <c>docs/prd/state-transitions.md</c> section 2.1 requires.
    /// </summary>
    private async Task<Result<(OrderDraft Draft, OrderDraftGarment Garment)>> LoadGarmentForChangeAsync(
        Guid draftId,
        Guid garmentId,
        Guid organisationId,
        EntityTag expected,
        CancellationToken cancellationToken)
    {
        var draft = await store.FindAsync(draftId, organisationId, cancellationToken);

        if (draft is null)
        {
            return Result.Failure<(OrderDraft, OrderDraftGarment)>(OrdersErrors.DraftNotFound);
        }

        if (draft.FindGarment(garmentId) is not { } garment)
        {
            return Result.Failure<(OrderDraft, OrderDraftGarment)>(OrdersErrors.GarmentNotOnDraft);
        }

        return expected.Matches(store.EntityTagOf(garment))
            ? Result.Success((draft, garment))
            : Result.Failure<(OrderDraft, OrderDraftGarment)>(OrdersErrors.ConcurrentChange);
    }
}
