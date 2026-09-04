using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security.Authorisation;

/// <summary>
/// Turns <c>perm:&lt;key&gt;</c> policy names into policies on demand, so a module declares a permission
/// once in its catalogue and uses it on an endpoint without registering a policy by hand.
/// </summary>
/// <param name="options">The static authorisation options, used for policies registered explicitly.</param>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    /// <inheritdoc />
    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(policyName);

        if (!PermissionPolicy.IsPermissionPolicy(policyName))
        {
            return await _fallback.GetPolicyAsync(policyName);
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(PermissionPolicy.KeyFrom(policyName)))
            .Build();
    }
}
