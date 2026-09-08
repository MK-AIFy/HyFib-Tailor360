namespace Tailor360.Modules.Customers.Domain.Customers;

/// <summary>
/// A branch that sees this customer in ordinary search results.
/// </summary>
/// <remarks>
/// <para>
/// A row rather than an array column, for two reasons that both matter at a counter. It carries
/// <see cref="AddedAt"/>, so the trail can answer "when did Branch B start serving this customer",
/// which is the cross-branch event <c>branch-scenarios.md</c> section 3.1 requires to be recorded;
/// and a unique index on the pair is what makes "a branch is added once" a fact about the database
/// rather than a hope about the code.
/// </para>
/// <para>
/// Append-only in practice — a branch is added when it begins serving the customer and nothing
/// removes it — so it carries no concurrency token of its own.
/// </para>
/// </remarks>
public sealed class CustomerBranchVisibility
{
    private CustomerBranchVisibility()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private CustomerBranchVisibility(Guid customerId, Guid branchId, DateTimeOffset addedAt, Guid? addedBy)
    {
        CustomerId = customerId;
        BranchId = branchId;
        AddedAt = addedAt;
        AddedBy = addedBy;
    }

    /// <summary>The customer.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The branch that sees the record.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>When the branch was added.</summary>
    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>Who added it, where a person did. Null for the branch that created the record.</summary>
    public Guid? AddedBy { get; private set; }

    /// <summary>Adds a branch. Called by the customer aggregate, never directly.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="branchId">The branch.</param>
    /// <param name="addedAt">The instant, from <c>IClock</c>.</param>
    /// <param name="addedBy">The actor.</param>
    /// <returns>The row.</returns>
    internal static CustomerBranchVisibility Add(
        Guid customerId,
        Guid branchId,
        DateTimeOffset addedAt,
        Guid? addedBy)
        => new(customerId, branchId, addedAt, addedBy);
}
