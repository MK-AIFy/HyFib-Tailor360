using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Domain.Jobs;

/// <summary>
/// The re-validated and re-priced position of one garment job inside an order revision.
/// </summary>
/// <remarks>
/// <para>
/// A separate type from <see cref="GarmentJobSpecification"/> on purpose, and the difference is the point. A
/// revision <em>names</em> a job that already exists and replaces its snapshots; it never creates one, never
/// changes its number, its index, its category or its dependencies. Reusing the specification here would have
/// published a way to revise a job into a different garment — and INV-ORD-03 says the display number is allocated
/// once and never re-pointed.
/// </para>
/// <para>
/// The window this type is usable in is narrow: <c>Order.Revise</c> is permitted only while <strong>every</strong>
/// garment job is still <see cref="GarmentJobStatus.Confirmed"/> and none has entered production (INV-ORD-05).
/// After that the only route is an alteration request (issue #34).
/// </para>
/// </remarks>
public sealed record GarmentJobRevision
{
    private GarmentJobRevision(
        Guid garmentJobId,
        MeasurementSnapshot measurements,
        DesignSnapshot design,
        PriceSnapshot price,
        DateOnly dueDate)
    {
        GarmentJobId = garmentJobId;
        Measurements = measurements;
        Design = design;
        Price = price;
        DueDate = dueDate;
    }

    /// <summary>The job this revises. Must already be on the order.</summary>
    public Guid GarmentJobId { get; }

    /// <summary>The measurement copy that replaces the frozen one.</summary>
    public MeasurementSnapshot Measurements { get; }

    /// <summary>The design copy that replaces the frozen one.</summary>
    public DesignSnapshot Design { get; }

    /// <summary>The priced result that replaces the frozen one. Display and printing only (INV-ORD-07).</summary>
    public PriceSnapshot Price { get; }

    /// <summary>The promised date as at this revision, evaluated in the branch timezone by the caller.</summary>
    public DateOnly DueDate { get; }

    /// <summary>
    /// Validates one garment of an order revision.
    /// </summary>
    /// <param name="garmentJobId">The job being revised.</param>
    /// <param name="measurements">The measurement copy that replaces the frozen one.</param>
    /// <param name="design">The design copy that replaces the frozen one.</param>
    /// <param name="price">The priced result that replaces the frozen one.</param>
    /// <param name="dueDate">The promised date as at this revision.</param>
    /// <returns>The revision, or the first failure found.</returns>
    public static Result<GarmentJobRevision> Create(
        Guid garmentJobId,
        MeasurementSnapshot measurements,
        DesignSnapshot design,
        PriceSnapshot price,
        DateOnly dueDate)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(price);

        if (garmentJobId == Guid.Empty)
        {
            return Result.Failure<GarmentJobRevision>(OrdersErrors.Required("garmentJobId"));
        }

        return Result.Success(new GarmentJobRevision(garmentJobId, measurements, design, price, dueDate));
    }
}
