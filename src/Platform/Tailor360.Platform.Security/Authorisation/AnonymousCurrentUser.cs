using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// The fail-closed <see cref="ICurrentUser"/>: nobody, holding nothing, assigned to no branch. Every
/// authorisation decision made against this user denies.
/// </summary>
/// <remarks>
/// The web host resolves <see cref="SessionCurrentUser"/> instead, which answers exactly as this type
/// does whenever no session was resolved. This one remains for a host with no request pipeline at all —
/// the worker and the command-line tool — where a principal is occasionally needed and the only honest
/// answer is "nobody". A host that needs a system or operator identity registers that explicitly rather
/// than widening what this type reports.
/// </remarks>
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
    public bool IsSignInComplete => false;

    /// <inheritdoc />
    public DateTimeOffset? LastReauthenticatedAt => null;

    /// <inheritdoc />
    public bool HasPermission(string permissionKey) => false;

    /// <inheritdoc />
    public bool CanActInBranch(Guid branchId) => false;
}
