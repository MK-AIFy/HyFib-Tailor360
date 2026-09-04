using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Platform.Security;

/// <summary>Registers the authorisation model shared by every module.</summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the permission catalogue, the permission and branch-scope handlers and the policy
    /// provider that materialises <c>perm:</c> policies.
    /// </summary>
    public static IServiceCollection AddTailor360Security(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<StepUpOptions>()
            .BindConfiguration(StepUpOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Fail closed: until #23 wires session-backed identity, every authorisation decision is made
        // against a principal that holds nothing.
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();

        services.AddSingleton<IPermissionSource, PlatformPermissions>();
        services.TryAddSingleton<PermissionCatalogue>();
        services.TryAddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorisationHandler>();
        services.AddScoped<IAuthorizationHandler, BranchScopeAuthorisationHandler>();
        // The scheme exists from the scaffold onwards so that the pipeline is complete and ordered
        // correctly; #23 replaces the handler with the session-backed one.
        services.AddAuthentication(SessionAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, UnconfiguredSessionAuthenticationHandler>(
                SessionAuthenticationDefaults.Scheme, displayName: null, configureOptions: null);

        services.AddAuthorization();

        return services;
    }
}
