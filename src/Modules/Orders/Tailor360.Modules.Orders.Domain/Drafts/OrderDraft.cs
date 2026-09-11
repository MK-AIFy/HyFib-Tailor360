using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Drafts;

/// <summary>
/// An order being built at the counter. Work in progress, and never an obligation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is shared within the branch.</strong> Every user holding <c>orders.intake</c> sees it and may
/// carry it on, because one person selects the customer, another photographs the material and a third finishes
/// the garment sections (<c>docs/prd/state-transitions.md</c> section 2.1). Sharing is what makes the lock
/// per garment section rather than per draft: the section is the unit somebody finishes, and last-writer-wins
/// over the whole draft loses a garment that nobody notices until the tailor does.
/// </para>
/// <para>
/// <strong>It expires, and expiring to nothing is a correct outcome.</strong> The window is branch
/// configuration and arrives as a parameter; <see cref="DefaultLifetime"/> is the documented default. An
/// expired draft is removed by the retention job, which makes it one of the very few things this system hard
/// deletes — G-5 limits hard deletion to approved classes and expired drafts are one of them
/// (<c>docs/architecture/invariants.md</c> G-5). Nothing is lost by that: a draft has burned no display
/// number, because a number is allocated at confirmation and never before
/// (<c>docs/architecture/conventions.md</c> section 3.2 rule 3).
/// </para>
/// <para>
/// <strong>It is consumed exactly once</strong>, which is what makes a confirmation retried after a lost
/// answer reach the order it already made rather than make a second one. The guard is here and is repeated as
/// a conditional update in the store, because a domain check alone loses the race it exists for.
/// </para>
/// </remarks>
public sealed class OrderDraft
{
    /// <summary>
    /// The documented default window a draft stays work in progress for.
    /// </summary>
    /// <remarks>
    /// 72 hours, from <c>docs/prd/glossary.md</c> section 4 and <c>docs/prd/state-transitions.md</c>
    /// section 2.1. The window itself is branch configuration, so it arrives at <see cref="Start"/> as a
    /// parameter; this constant is the default the application binds when a branch has configured none, and it
    /// lives here so that the number has exactly one home.
    /// </remarks>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(72);

    /// <summary>The longest note the column holds.</summary>
    public const int MaximumNotesLength = 2000;

    private readonly List<OrderDraftGarment> _garments = [];

    private OrderDraft()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private OrderDraft(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        Guid? by)
    {
        Id = id;
        OrganisationId = organisationId;
        BranchId = branchId;
        CustomerId = customerId;
        StartedAt = now;
        StartedBy = by;
        UpdatedAt = now;
        UpdatedBy = by;
        ExpiresAt = expiresAt;
    }

    /// <summary>Identity of this draft. A UUIDv7, and the only identifier that appears in a path.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organisation the draft belongs to.</summary>
    public Guid OrganisationId { get; private set; }

    /// <summary>
    /// The branch taking the order. Drafts are shared within it, with every user holding
    /// <c>orders.intake</c>.
    /// </summary>
    public Guid BranchId { get; private set; }

    /// <summary>
    /// The customer the draft is being built for. An identifier: no name, no telephone number, no address.
    /// </summary>
    public Guid CustomerId { get; private set; }

    /// <summary>
    /// The promised date for the order as a whole, evaluated in the branch timezone against the branch working
    /// calendar by the caller (<c>docs/architecture/conventions.md</c> section 2.2).
    /// </summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>What the counter wrote about the order as a whole.</summary>
    public string? Notes { get; private set; }

    /// <summary>When the draft was started, in UTC.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Who started it.</summary>
    public Guid? StartedBy { get; private set; }

    /// <summary>When it was last written to, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Who last wrote to it.</summary>
    public Guid? UpdatedBy { get; private set; }

    /// <summary>When it stops being work in progress, in UTC.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it became an order, or null while it is still work in progress.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>
    /// The garment sections, in <see cref="OrderDraftGarment.Position"/> order.
    /// </summary>
    /// <remarks>
    /// The aggregate only ever appends a section at the end and removes one in place, so the order it holds
    /// them in is position order and stays so; a reader that materialises the collection itself is responsible
    /// for ordering it the same way.
    /// </remarks>
    public IReadOnlyList<OrderDraftGarment> Garments => _garments;

    /// <summary>
    /// Whether the draft has yet to become an order.
    /// </summary>
    /// <remarks>
    /// Consumption only, and on its own it is <strong>not</strong> the question "may this be written to":
    /// whether the draft has also outlived its window is a question about an instant, so it is
    /// <see cref="IsExpiredAt"/> and is asked with a clock in hand. Every command on this aggregate asks
    /// both.
    /// </remarks>
    public bool IsOpen => ConsumedAt is null;

    /// <summary>Starts a draft.</summary>
    /// <param name="id">Identity of the draft, from <c>IIdGenerator</c>.</param>
    /// <param name="organisationId">The organisation.</param>
    /// <param name="branchId">The branch taking the order.</param>
    /// <param name="customerId">The customer it is being built for.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="lifetime">How long the branch lets a draft stay work in progress.</param>
    /// <param name="by">The actor.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public static Result<OrderDraft> Start(
        Guid id,
        Guid organisationId,
        Guid branchId,
        Guid customerId,
        DateTimeOffset now,
        TimeSpan lifetime,
        Guid? by = null)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<OrderDraft>(OrdersErrors.Required("id"));
        }

        if (organisationId == Guid.Empty)
        {
            return Result.Failure<OrderDraft>(OrdersErrors.Required("organisationId"));
        }

        // Branch scope is evaluated, never inferred (CLAUDE.md security rule 2). A draft with no branch could
        // not be shared with the right people, could not be swept by the right retention job, and would leave
        // the confirmation with no sequence to take its display number from.
        if (branchId == Guid.Empty)
        {
            return Result.Failure<OrderDraft>(OrdersErrors.NoBranchInContext);
        }

        if (customerId == Guid.Empty)
        {
            return Result.Failure<OrderDraft>(OrdersErrors.Required("customerId"));
        }

        // A window of zero or less would make the draft expired the moment it was started, so the person at
        // the counter would be refused by the next command with no way to tell why.
        if (lifetime <= TimeSpan.Zero)
        {
            return Result.Failure<OrderDraft>(OrdersErrors.DraftLifetimeNotPositive);
        }

        return Result.Success(
            new OrderDraft(id, organisationId, branchId, customerId, now, now.Add(lifetime), by));
    }

    /// <summary>
    /// Points the draft at a different customer.
    /// </summary>
    /// <remarks>
    /// What correcting a mis-selection at the counter does, and it is ordinary: the wrong Lakshmi was chosen
    /// from the search results and somebody noticed before confirming. The garment sections are kept, because
    /// they describe the garments and not the person.
    /// </remarks>
    /// <param name="customerId">The customer the draft is really for.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetCustomer(Guid customerId, DateTimeOffset now, Guid? by)
    {
        var writable = EnsureWritable(now);
        if (writable.IsFailure)
        {
            return writable;
        }

        if (customerId == Guid.Empty)
        {
            return Result.Failure(OrdersErrors.Required("customerId"));
        }

        CustomerId = customerId;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Sets the order-level promised date and notes.</summary>
    /// <param name="dueDate">The promised date, as a branch-local date, or null to clear it.</param>
    /// <param name="notes">What the counter wrote, or null to clear it.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetSchedule(DateOnly? dueDate, string? notes, DateTimeOffset now, Guid? by)
    {
        var writable = EnsureWritable(now);
        if (writable.IsFailure)
        {
            return writable;
        }

        var written = notes?.Trim();
        if (string.IsNullOrEmpty(written))
        {
            written = null;
        }
        else if (written.Length > MaximumNotesLength)
        {
            return Result.Failure(OrdersErrors.TooLong("notes", MaximumNotesLength));
        }

        DueDate = dueDate;
        Notes = written;
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Adds a garment section at the next position.</summary>
    /// <param name="garmentId">Identity of the section, from <c>IIdGenerator</c>.</param>
    /// <param name="content">The validated content.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>The section, or the reason it could not be added.</returns>
    public Result<OrderDraftGarment> AddGarment(
        Guid garmentId,
        OrderDraftGarmentContent content,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(content);

        var writable = EnsureWritable(now);
        if (writable.IsFailure)
        {
            return Result.Failure<OrderDraftGarment>(writable.Error);
        }

        if (garmentId == Guid.Empty)
        {
            return Result.Failure<OrderDraftGarment>(OrdersErrors.Required("garmentId"));
        }

        // A retried add that reuses the identifier of a section already on the draft is refused rather than
        // treated as a save: the two requests may describe different garments, and quietly overwriting one
        // with the other is the loss the per-section lock exists to prevent.
        if (FindGarment(garmentId) is not null)
        {
            return Result.Failure<OrderDraftGarment>(OrdersErrors.GarmentAlreadyOnDraft);
        }

        var garment = OrderDraftGarment.Add(garmentId, Id, NextPosition(), content, now, by);

        _garments.Add(garment);
        Touch(now, by);

        return Result.Success(garment);
    }

    /// <summary>
    /// Replaces one garment section, leaving every other section alone.
    /// </summary>
    /// <remarks>
    /// This is the unit the per-section <c>If-Match</c> lock protects. Two people editing one draft is the
    /// ordinary case in a shop, and the loser of a race on the same section gets <c>409</c> with the current
    /// version rather than having their work silently overwritten
    /// (<c>docs/prd/state-transitions.md</c> section 2.1).
    /// </remarks>
    /// <param name="garmentId">The section being saved.</param>
    /// <param name="content">The validated new content.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the save was refused.</returns>
    public Result SaveGarment(
        Guid garmentId,
        OrderDraftGarmentContent content,
        DateTimeOffset now,
        Guid? by)
    {
        ArgumentNullException.ThrowIfNull(content);

        var writable = EnsureWritable(now);
        if (writable.IsFailure)
        {
            return writable;
        }

        if (FindGarment(garmentId) is not { } garment)
        {
            return Result.Failure(OrdersErrors.GarmentNotOnDraft);
        }

        garment.Apply(content, now, by);
        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Removes a garment section and every dependency naming it, in either direction.
    /// </summary>
    /// <remarks>
    /// Both directions, because a dependency is a statement about two garments and only one of them holds the
    /// row. Leaving the other half behind would carry a prerequisite naming a garment the order does not have
    /// into <c>job_dependencies</c> at confirmation, where it would block a job against nothing (INV-JOB-09).
    /// </remarks>
    /// <param name="garmentId">The section being removed.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the removal was refused.</returns>
    public Result RemoveGarment(Guid garmentId, DateTimeOffset now, Guid? by)
    {
        var writable = EnsureWritable(now);
        if (writable.IsFailure)
        {
            return writable;
        }

        if (FindGarment(garmentId) is not { } garment)
        {
            return Result.Failure(OrdersErrors.GarmentNotOnDraft);
        }

        _garments.Remove(garment);

        foreach (var remaining in _garments)
        {
            remaining.WithdrawDependenciesNaming(garmentId, now, by);
        }

        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Declares that one section waits for, or is delivered with, another section of the same draft.
    /// </summary>
    /// <remarks>
    /// Both ends must be on this draft. A dependency across two drafts could not survive confirmation, which
    /// turns exactly one draft into exactly one order, and INV-JOB-09 is a statement about two jobs of the
    /// <em>same</em> order.
    /// </remarks>
    /// <param name="garmentId">The dependent section.</param>
    /// <param name="prerequisiteGarmentId">The section it depends on.</param>
    /// <param name="kind">Which relationship is being declared.</param>
    /// <param name="reason">Why, where a reason was given.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the declaration was refused.</returns>
    public Result DeclareDependency(
        Guid garmentId,
        Guid prerequisiteGarmentId,
        JobDependencyKind kind,
        string? reason,
        DateTimeOffset now,
        Guid? by)
    {
        var writable = EnsureWritable(now);
        if (writable.IsFailure)
        {
            return writable;
        }

        if (FindGarment(garmentId) is not { } garment)
        {
            return Result.Failure(OrdersErrors.GarmentNotOnDraft);
        }

        if (FindGarment(prerequisiteGarmentId) is null)
        {
            return Result.Failure(OrdersErrors.GarmentNotOnDraft);
        }

        var declared = garment.DeclareDependency(prerequisiteGarmentId, kind, reason, now, by);
        if (declared.IsFailure)
        {
            return declared;
        }

        Touch(now, by);

        return Result.Success();
    }

    /// <summary>Withdraws a dependency one section declared on another.</summary>
    /// <param name="garmentId">The dependent section.</param>
    /// <param name="prerequisiteGarmentId">The section it named.</param>
    /// <param name="kind">The relationship that was declared.</param>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <param name="by">The actor.</param>
    /// <returns>Success, or the reason the withdrawal was refused.</returns>
    public Result WithdrawDependency(
        Guid garmentId,
        Guid prerequisiteGarmentId,
        JobDependencyKind kind,
        DateTimeOffset now,
        Guid? by)
    {
        var writable = EnsureWritable(now);
        if (writable.IsFailure)
        {
            return writable;
        }

        if (FindGarment(garmentId) is not { } garment)
        {
            return Result.Failure(OrdersErrors.GarmentNotOnDraft);
        }

        var withdrawn = garment.WithdrawDependency(prerequisiteGarmentId, kind, now, by);
        if (withdrawn.IsFailure)
        {
            return withdrawn;
        }

        Touch(now, by);

        return Result.Success();
    }

    /// <summary>
    /// Marks the draft as spent, so it can never become a second order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The expiry is checked here and not only by the retention job, because a draft that outlived its window
    /// and has not yet been swept is still too old to confirm: the prices, the availability and the promised
    /// dates in it were agreed three days ago.
    /// </para>
    /// <para>
    /// A draft with no garment sections is refused last, once the state questions are answered, because an
    /// order with no garments is not a commitment to anything and there would be nothing to make a job card
    /// from (<c>docs/prd/state-transitions.md</c> section 2.1).
    /// </para>
    /// <para>
    /// A garment with no measurement decision is refused after that. Section 2.1 makes "every garment has a
    /// confirmed measurement version or an explicit reuse" a precondition of the confirmation itself, and
    /// the draft is the only thing that can see it — <c>Order.Confirm</c> never sees a draft at all, so
    /// with the guard missing the precondition was enforced nowhere. The refusal here names no garment;
    /// <see cref="GarmentsWithoutMeasurements"/> stays the way the application builds the field error that
    /// names them all at once, which is the shape section 2.1 asks for.
    /// </para>
    /// </remarks>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>Success, or the reason the draft could not be consumed.</returns>
    public Result Consume(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Result.Failure(OrdersErrors.DraftAlreadyConfirmed);
        }

        if (IsExpiredAt(now))
        {
            return Result.Failure(OrdersErrors.DraftExpired);
        }

        if (_garments.Count == 0)
        {
            return Result.Failure(OrdersErrors.DraftHasNoGarments);
        }

        if (GarmentsWithoutMeasurements().Count > 0)
        {
            return Result.Failure(OrdersErrors.GarmentMeasurementNotDecided);
        }

        ConsumedAt = now;

        return Result.Success();
    }

    /// <summary>Whether the draft has outlived its window at this instant.</summary>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>True once the window has closed.</returns>
    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>
    /// The sections confirmation would refuse, in display order.
    /// </summary>
    /// <remarks>
    /// Returned as sections rather than as a boolean so the application can answer with one field error
    /// listing every garment that has neither a confirmed measurement version nor an explicit reuse — naming
    /// them all at once, rather than sending somebody back to the counter once per garment
    /// (<c>docs/prd/state-transitions.md</c> section 2.1, <c>docs/IMPLEMENTATION_PLAN.md</c> #32b).
    /// </remarks>
    /// <returns>The sections with no measurement decision.</returns>
    public IReadOnlyList<OrderDraftGarment> GarmentsWithoutMeasurements()
        => [.. _garments
            .Where(garment => !garment.HasMeasurementDecision)
            .OrderBy(garment => garment.Position)];

    /// <summary>One garment section by its identity, or null when it is not on this draft.</summary>
    /// <param name="garmentId">The section.</param>
    /// <returns>The section, or null.</returns>
    public OrderDraftGarment? FindGarment(Guid garmentId)
        => _garments.Find(garment => garment.Id == garmentId);

    /// <summary>
    /// Whether the draft may be written to at this instant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both questions, and the expiry is the one that used to be missing. <see cref="Consume"/> checked it
    /// and the writers did not, so the counter could photograph material, add garments and declare
    /// dependencies onto a draft that was already too old to confirm — and find out only when somebody
    /// pressed confirm, with the whole session's work to do again. The reasoning
    /// <see cref="Consume"/> gives for refusing an unswept expired draft is a reason not to build on one
    /// either: the prices, the availability and the promised dates in it were agreed three days ago.
    /// </para>
    /// <para>
    /// Consumption is asked about first, because "this draft has already become an order — open the
    /// order" is the more useful of the two answers when both are true.
    /// </para>
    /// </remarks>
    /// <param name="now">The instant, from <c>IClock</c>.</param>
    /// <returns>Success, or the reason the draft can no longer be written to.</returns>
    private Result EnsureWritable(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Result.Failure(OrdersErrors.DraftAlreadyConfirmed);
        }

        return IsExpiredAt(now)
            ? Result.Failure(OrdersErrors.DraftExpired)
            : Result.Success();
    }

    private int NextPosition()
        => _garments.Count == 0 ? 1 : _garments.Max(garment => garment.Position) + 1;

    private void Touch(DateTimeOffset now, Guid? by)
    {
        UpdatedAt = now;
        UpdatedBy = by;
    }
}
