using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.Modules.Identity.Application.Mfa;

/// <summary>
/// The shipped requirement policy: three independent reasons, any one of which is enough.
/// </summary>
/// <remarks>
/// The three are checked in the order they are cheapest to explain, and the reason string names which
/// one fired, because "you must set up an authenticator" without a reason is the kind of message that
/// generates a support call.
/// <list type="number">
/// <item><description>The deployment requires it of everyone.</description></item>
/// <item><description>The account holds one of the configured roles — Owner, Admin and Cashier by
/// default, per #23.</description></item>
/// <item><description>The account holds a permission under a configured prefix
/// (<c>admin.</c>, <c>billing.</c>), or one the catalogue marks <c>RequiresMfa</c>. The catalogue is
/// consulted as well as the prefixes because #24 flags individual permissions such as
/// <c>payments.refund</c> that no prefix would catch.</description></item>
/// </list>
/// </remarks>
/// <param name="options">The configured requirement.</param>
/// <param name="catalogue">The permission catalogue, for the per-permission flag.</param>
public sealed class MfaRequirementPolicy(
    IOptions<MfaOptions> options,
    PermissionCatalogue catalogue) : IMfaRequirementPolicy
{
    private readonly MfaOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public bool IsRequiredFor(IEnumerable<string> roles, IEnumerable<string> permissions)
        => ReasonFor(roles, permissions) is not null;

    /// <inheritdoc />
    public string? ReasonFor(IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(permissions);

        if (_options.RequiredForEveryone)
        {
            return "every account in this deployment";
        }

        var required = _options.EffectiveRequiredRoles;
        foreach (var role in roles)
        {
            if (role is not null && required.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                return $"the {role} role";
            }
        }

        var prefixes = _options.EffectiveRequiredPermissionPrefixes;
        foreach (var permission in permissions)
        {
            if (permission is null)
            {
                continue;
            }

            foreach (var prefix in prefixes)
            {
                if (permission.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return $"the {permission} permission";
                }
            }

            if (catalogue?.Find(permission) is { RequiresMfa: true })
            {
                return $"the {permission} permission";
            }
        }

        return null;
    }
}
