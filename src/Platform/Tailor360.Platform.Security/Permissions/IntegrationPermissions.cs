namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Integration module: outbound webhook subscriptions and their delivery.
/// </summary>
/// <remarks>
/// A webhook subscription names an external destination for this shop's data, and a replay sends that
/// data again. Both are flagged for multi-factor authentication and carry a mandatory reason.
/// </remarks>
public static class IntegrationPermissions
{
    /// <summary>Create, change and revoke outbound webhook subscriptions.</summary>
    public const string ManageWebhooks = "integration.manage_webhooks";

    /// <summary>Replay a failed webhook delivery.</summary>
    public const string ReplayDelivery = "integration.replay_delivery";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(ManageWebhooks, "Create, change and revoke outbound webhook subscriptions.",
            PermissionModules.Integration, PermissionScope.Organisation,
            RequiresMfa: true, RequiresStepUp: true, RequiresReason: true),
        new(ReplayDelivery, "Replay a failed webhook delivery.",
            PermissionModules.Integration, PermissionScope.Organisation, RequiresMfa: true, RequiresReason: true),
    ];
}
