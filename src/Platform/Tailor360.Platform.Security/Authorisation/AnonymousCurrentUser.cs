using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// The fail-closed default <see cref="ICurrentUser"/>: nobody, holding nothing, assigned to no branch.
/// It is registered with <c>TryAdd</c> so that a host which has not yet wired authentication cannot
/// accidentally resolve a permissive principal; issue #23 replaces it with the session-backed
/// implementation. Every authorisation decision made against this user denies.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public bool IsAuthenticated => false;

    /// <inheritdoc />
    public Guid UserId => Guid.Empty;

    /// <inheritdoc />
    public string PrincipalId => "anonymous";

    /// <inheritdoc />
    public string DisplayName => "Anonymous";

    /// <inheritdoc />
    public OrganisationContext Context { get; } = new(Guid.Empty, null);

    /// <inheritdoc />
    public IReadOnlySet<Guid> AssignedBranches { get; } = new HashSet<Guid>();

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool MfaSatisfied => false;

    /// <inheritdoc />
    public DateTimeOffset? LastReauthenticatedAt => null;

    /// <inheritdoc />
    public bool HasPermission(string permissionKey) => false;

    /// <inheritdoc />
    public bool CanActInBranch(Guid branchId) => false;
}
