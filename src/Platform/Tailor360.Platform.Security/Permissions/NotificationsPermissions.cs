namespace Tailor360.Platform.Security.Permissions;

/// <summary>
/// Permissions owned by the Notifications and Feedback module: message templates, delivery replay,
/// and the feedback and service-recovery queue.
/// </summary>
/// <remarks>
/// Replaying a delivery can send a customer a second message, and changing a template changes what
/// every future message says, so both are flagged. Reading feedback is not: a response written by a
/// customer through a one-time link is read by the branch that has to act on it.
/// </remarks>
public static class NotificationsPermissions
{
    /// <summary>Create and change notification templates in every supported language.</summary>
    public const string ManageTemplates = "notifications.manage_templates";

    /// <summary>Replay a failed notification delivery.</summary>
    public const string Replay = "notifications.replay";

    /// <summary>Read customer feedback responses and their scores.</summary>
    public const string ReadFeedback = "feedback.read";

    /// <summary>Own a service-recovery case: contact the customer, decide the remedy, close it.</summary>
    public const string ManageCases = "feedback.manage_cases";

    /// <summary>Every permission in this group, in declaration order.</summary>
    public static IReadOnlyCollection<Permission> All { get; } =
    [
        new(ManageTemplates, "Create and change notification templates.",
            PermissionModules.Notifications, PermissionScope.Organisation, RequiresMfa: true, RequiresReason: true),
        new(Replay, "Replay a failed notification delivery.",
            PermissionModules.Notifications, PermissionScope.Organisation, RequiresMfa: true, RequiresReason: true),
        new(ReadFeedback, "Read customer feedback responses and their scores.",
            PermissionModules.Notifications),
        new(ManageCases, "Own and close a service-recovery case.",
            PermissionModules.Notifications, RequiresReason: true),
    ];
}
