namespace Tailor360.Modules.Identity.Domain.Access;

/// <summary>
/// One role held by one account. The pair is the key: an account either holds a role or does not, and
/// holding it twice is not a state the store can represent.
/// </summary>
public sealed class UserRoleAssignment
{
    private UserRoleAssignment()
    {
        // The persistence layer materialises instances through this constructor.
    }

    private UserRoleAssignment(Guid userId, Guid roleId, DateTimeOffset now, Guid? by)
    {
        UserId = userId;
        RoleId = roleId;
        AssignedAt = now;
        AssignedBy = by;
    }

    /// <summary>The account.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The role.</summary>
    public Guid RoleId { get; private set; }

    /// <summary>When the role was granted.</summary>
    public DateTimeOffset AssignedAt { get; private set; }

    /// <summary>Who granted it.</summary>
    public Guid? AssignedBy { get; private set; }

    /// <summary>Grants a role to an account.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="now">The current instant, from <c>IClock</c>.</param>
    /// <param name="by">The administrator granting it.</param>
    public static UserRoleAssignment Create(Guid userId, Guid roleId, DateTimeOffset now, Guid? by = null)
        => new(userId, roleId, now, by);
}
