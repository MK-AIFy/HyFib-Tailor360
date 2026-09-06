namespace Tailor360.Modules.Identity.Application.Access;

/// <summary>
/// What one account may do: the roles it holds, the permissions those roles grant, and the branches it
/// may act in.
/// </summary>
/// <remarks>
/// It is resolved from the database on every authenticated request rather than cached in the session
/// cookie, which is what makes a permission removed a minute ago take effect on the next request
/// instead of at the next sign-in.
/// </remarks>
/// <param name="RoleNames">The display names of the roles held, for the multi-factor policy and the interface.</param>
/// <param name="Permissions">The permission keys the roles grant, de-duplicated.</param>
/// <param name="BranchIds">The branches the account is assigned to.</param>
public sealed record UserAccess(
    IReadOnlySet<string> RoleNames,
    IReadOnlySet<string> Permissions,
    IReadOnlySet<Guid> BranchIds)
{
    /// <summary>An account that holds nothing. The value every failure path resolves to.</summary>
    public static UserAccess None { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<Guid>());
}

/// <summary>Resolves an account's effective roles, permissions and branch assignments.</summary>
public interface IUserAccessQuery
{
    /// <summary>Reads the effective access of one account.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<UserAccess> ResolveAsync(Guid userId, CancellationToken cancellationToken = default);
}
