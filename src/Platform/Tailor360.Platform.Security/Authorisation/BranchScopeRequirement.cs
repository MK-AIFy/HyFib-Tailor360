using Microsoft.AspNetCore.Authorization;
using Tailor360.Platform.Abstractions.Multitenancy;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Requires that the caller is acting within a branch they are assigned to. Endpoints that read or
/// write branch-owned data carry this alongside their permission requirement, so holding a permission
/// never grants reach across branches on its own.
/// </summary>
/// <param name="scope">How wide the caller's reach must be.</param>
public sealed class BranchScopeRequirement(BranchScope scope) : IAuthorizationRequirement
{
    /// <summary>The reach the endpoint demands.</summary>
    public BranchScope Scope { get; } = scope;
}
