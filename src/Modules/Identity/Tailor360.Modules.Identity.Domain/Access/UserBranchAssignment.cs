namespace Tailor360.Modules.Identity.Domain.Access;

/// <summary>
/// One branch an account may act in.
/// </summary>
/// <remarks>
/// This is the record that branch scoping is evaluated against. A caller's claims say which branch they
/// are working in; these rows say which branches they are allowed to work in, and the branch-scope
/// handler compares the two on the server. An account with no rows here reaches no branch-owned data at
/// all, which is the fail-closed direction.
/// </remarks>
public sealed class UserBranchAssignment
{
    private UserBranchAssignment()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private UserBranchAssignment(Guid userId, Guid branchId, bool isPrimary, DateTimeOffset now, Guid? by)
    {
        UserId = userId;
        BranchId = branchId;
        IsPrimary = isPrimary;
        AssignedAt = now;
        AssignedBy = by;
    }

    /// <summary>The account.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The branch.</summary>
    public Guid BranchId { get; private set; }

    /// <summary>True for the branch a session starts in by default.</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>When the assignment was made.</summary>
    public DateTimeOffset AssignedAt { get; private set; }

    /// <summary>Who made it.</summary>
    public Guid? AssignedBy { get; private set; }

    /// <summary>Assigns an account to a branch.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="branchId">The branch.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="isPrimary">True for the branch a session starts in.</param>
    /// <param name="by">The administrator making the assignment.</param>
    public static UserBranchAssignment Create(
        Guid userId,
        Guid branchId,
        DateTimeOffset now,
        bool isPrimary = false,
        Guid? by = null)
        => new(userId, branchId, isPrimary, now, by);
}
