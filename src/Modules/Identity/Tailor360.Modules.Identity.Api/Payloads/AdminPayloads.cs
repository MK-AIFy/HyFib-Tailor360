using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Administration;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.FeatureFlags;

namespace Tailor360.Modules.Identity.Api.Payloads;

/// <summary>One staff account as the administration screens show it.</summary>
/// <remarks>
/// Declared here rather than returning <c>AdministeredUser</c>, because a payload is a published shape
/// and an application type is free to change (ARCH-013). The version is carried in the body as well as
/// in the <c>ETag</c> header: a client that re-reads uses the header, and one that merges a list in
/// place uses the member, and sending only one of them costs the other a second round trip.
/// </remarks>
/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The name shown on screen.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="Email">The address invitations and security alerts are sent to.</param>
/// <param name="Status"><c>Invited</c>, <c>Active</c>, <c>Suspended</c> or <c>Deactivated</c>.</param>
/// <param name="MfaEnrolment">Whether a second factor is enrolled.</param>
/// <param name="HomeBranchId">The account's default branch, if it has one.</param>
/// <param name="LastSignInAt">When the account was last used, or null if it never has been.</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="Version">The version an edit must present in <c>If-Match</c>.</param>
public sealed record StaffUserPayload(
    Guid UserId,
    string DisplayName,
    string UserName,
    string Email,
    string Status,
    string MfaEnrolment,
    Guid? HomeBranchId,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt,
    string Version)
{
    /// <summary>Projects an administered account onto the wire.</summary>
    /// <param name="user">The account.</param>
    public static StaffUserPayload From(AdministeredUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new StaffUserPayload(
            user.UserId,
            user.DisplayName,
            user.UserName,
            user.Email,
            user.Status.ToString(),
            user.MfaEnrolment.ToString(),
            user.HomeBranchId,
            user.LastSignInAt,
            user.CreatedAt,
            user.Version.Version);
    }
}

/// <summary>The reason an administrator gives for a change.</summary>
/// <remarks>
/// <para>
/// A field in the body rather than a header, for two reasons. It is free text a person typed, so it
/// needs server-side validation and a refusal the screen can attach to the field they typed it in; and
/// a header is far more likely than a body to be copied into a proxy log or a request trace, which is
/// exactly where a reason naming a colleague must not end up.
/// </para>
/// <para>
/// The bound is the audit column's, so a reason that would be truncated on the way to the trail is
/// refused here instead — a reason silently cut in half is worse than one refused.
/// </para>
/// </remarks>
/// <param name="Reason">
/// Why the change is being made. Required, and no longer than
/// <see cref="AdminRequests.MaximumReasonLength"/> characters.
/// </param>
/// <remarks>
/// The bounds are enforced by the endpoint rather than declared as attributes on this record, so that
/// both refusals are this module's own: the same problem type, the same stable code, and a field error
/// naming <c>reason</c> that the screen can attach to the box the person typed in. Model binding would
/// answer with the framework's wording instead, which is neither localisable nor ours to change.
/// </remarks>
public sealed record ReasonPayload(string? Reason);

/// <summary>The bounds and problem codes the administrative surface shares.</summary>
public static class AdminRequests
{
    /// <summary>The shortest reason accepted, which rules out a single character or a full stop.</summary>
    public const int MinimumReasonLength = 3;

    /// <summary>The longest reason the audit trail stores.</summary>
    public const int MaximumReasonLength = 500;

    /// <summary>Returned when an administrative edit is made against a version that has moved on.</summary>
    public const string VersionConflict = "identity.version-conflict";

    /// <summary>What the person on the screen is told when their edit lost the race.</summary>
    public const string VersionConflictDetail =
        "Somebody else changed this account while you had it open. Reload it, check the change is still "
        + "the one you want, and apply it again.";
}

/// <summary>What an account may do and where, for an administrative screen.</summary>
/// <param name="RoleKeys">The roles it holds, by key.</param>
/// <param name="Branches">The branches it works in.</param>
/// <param name="Version">The version an edit must present in <c>If-Match</c>.</param>
public sealed record AssignedAccessPayload(
    IReadOnlyList<string> RoleKeys,
    IReadOnlyList<BranchAssignmentPayload> Branches,
    string Version)
{
    /// <summary>Projects an account's access onto the wire.</summary>
    /// <param name="access">The access.</param>
    public static AssignedAccessPayload From(AssignedAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);

        return new AssignedAccessPayload(
            access.RoleKeys,
            [.. access.Branches.Select(branch => new BranchAssignmentPayload(branch.BranchId, branch.IsPrimary))],
            access.Version.Version);
    }
}

/// <summary>One branch an account works in.</summary>
/// <param name="BranchId">The branch.</param>
/// <param name="IsPrimary">Whether it is the account's usual place of work.</param>
public sealed record BranchAssignmentPayload(Guid BranchId, bool IsPrimary);

/// <summary>The roles an account should hold after the change, with the reason for making it.</summary>
/// <remarks>
/// The whole set, not a list of additions. It is what the screen shows the administrator, and it is
/// what makes the audit entry a sentence rather than a difference somebody has to reconstruct.
/// </remarks>
/// <param name="RoleKeys">Every role the account should hold afterwards.</param>
/// <param name="Reason">Why the change is being made.</param>
public sealed record ReplaceRolesPayload(IReadOnlyList<string>? RoleKeys, string? Reason);

/// <summary>The branches an account should work in after the change, with the reason.</summary>
/// <param name="Branches">Every branch the account should work in afterwards.</param>
/// <param name="Reason">Why the change is being made.</param>
public sealed record ReplaceBranchesPayload(
    IReadOnlyList<BranchAssignmentPayload>? Branches,
    string? Reason);

/// <summary>One page of staff accounts.</summary>
/// <param name="Users">The accounts on this page, oldest first.</param>
/// <param name="NextCursor">
/// Pass this back as <c>cursor</c> to read the next page, or null when this is the last one.
/// </param>
public sealed record StaffPagePayload(IReadOnlyList<StaffSummaryPayload> Users, string? NextCursor)
{
    /// <summary>Projects a page onto the wire.</summary>
    /// <param name="page">The page.</param>
    public static StaffPagePayload From(StaffPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new StaffPagePayload(
            [.. page.Users.Select(StaffSummaryPayload.From)],
            page.NextCursor);
    }
}

/// <summary>One account in an administrative list.</summary>
/// <remarks>
/// No email address. The list is a screen an administrator scans, and an address is only needed on the
/// record itself — so the endpoint that returns a hundred accounts at a time returns none of them.
/// </remarks>
/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The name shown on screen.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="Status"><c>Invited</c>, <c>Active</c>, <c>Suspended</c> or <c>Deactivated</c>.</param>
/// <param name="MfaEnrolment">Whether a second factor is enrolled.</param>
/// <param name="HomeBranchId">Its default branch, if it has one.</param>
/// <param name="RoleKeys">The roles it holds.</param>
/// <param name="LastSignInAt">When it was last used, or null if it never has been.</param>
/// <param name="CreatedAt">When it was created.</param>
public sealed record StaffSummaryPayload(
    Guid UserId,
    string DisplayName,
    string UserName,
    string Status,
    string MfaEnrolment,
    Guid? HomeBranchId,
    IReadOnlyList<string> RoleKeys,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>Projects one summary onto the wire.</summary>
    /// <param name="user">The account.</param>
    public static StaffSummaryPayload From(StaffSummary user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new StaffSummaryPayload(
            user.UserId,
            user.DisplayName,
            user.UserName,
            user.Status.ToString(),
            user.MfaEnrolment.ToString(),
            user.HomeBranchId,
            user.RoleKeys,
            user.LastSignInAt,
            user.CreatedAt);
    }
}

/// <summary>Who to invite, and why.</summary>
/// <remarks>
/// The organisation is not a field. It is taken from the caller's own session, because an
/// administrator invites into their organisation and nowhere else, and a request that could name one
/// would be a request worth trying against somebody else's.
/// </remarks>
/// <param name="UserName">The sign-in name the person will use.</param>
/// <param name="Email">Where the invitation is sent.</param>
/// <param name="DisplayName">The name colleagues will see.</param>
/// <param name="HomeBranchId">The branch they usually work in, if it is known yet.</param>
/// <param name="Reason">Why the account is being created.</param>
public sealed record InviteStaffMemberPayload(
    string? UserName,
    string? Email,
    string? DisplayName,
    Guid? HomeBranchId,
    string? Reason);

/// <summary>One branch as an administrative screen sees it.</summary>
/// <param name="BranchId">The branch.</param>
/// <param name="Code">Its short code, which appears in document numbers and never changes.</param>
/// <param name="Name">Its name.</param>
/// <param name="TimeZoneId">The IANA timezone its due dates are computed in.</param>
/// <param name="Status"><c>Active</c> or <c>Inactive</c>.</param>
/// <param name="StatusReason">Why it was last opened or closed.</param>
/// <param name="AddressLine1">First line of the street address.</param>
/// <param name="AddressLine2">Second line of the street address.</param>
/// <param name="City">The town or city.</param>
/// <param name="State">The state.</param>
/// <param name="PostalCode">The postal code.</param>
/// <param name="ContactPhone">The number customers and couriers call.</param>
/// <param name="ContactEmail">The address customer correspondence comes from.</param>
/// <param name="GstRegistrationReference">Which GST registration the branch trades under.</param>
/// <param name="Version">The version an edit must present in <c>If-Match</c>.</param>
public sealed record BranchPayload(
    Guid BranchId,
    string Code,
    string Name,
    string TimeZoneId,
    string Status,
    string? StatusReason,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? ContactPhone,
    string? ContactEmail,
    string? GstRegistrationReference,
    string Version)
{
    /// <summary>Projects a branch onto the wire.</summary>
    /// <param name="branch">The branch.</param>
    public static BranchPayload From(AdministeredBranch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        return new BranchPayload(
            branch.BranchId,
            branch.Code,
            branch.Name,
            branch.TimeZoneId,
            branch.Status,
            branch.StatusReason,
            branch.Details.AddressLine1,
            branch.Details.AddressLine2,
            branch.Details.City,
            branch.Details.State,
            branch.Details.PostalCode,
            branch.Details.ContactPhone,
            branch.Details.ContactEmail,
            branch.Details.GstRegistrationReference,
            branch.Version.Version);
    }
}

/// <summary>A branch to open.</summary>
/// <remarks>
/// The code is here and nowhere else. It is embedded in every order, estimate and invoice number the
/// branch will ever produce, so it is chosen once and never again — which is why reconfiguring takes a
/// different shape rather than this one with a field that silently does nothing.
/// </remarks>
/// <param name="Code">The short code that will appear in the branch's document numbers.</param>
/// <param name="Name">The branch name.</param>
/// <param name="TimeZoneId">The IANA timezone its due dates are computed in.</param>
/// <param name="AddressLine1">First line of the street address.</param>
/// <param name="AddressLine2">Second line of the street address.</param>
/// <param name="City">The town or city.</param>
/// <param name="State">The state.</param>
/// <param name="PostalCode">The postal code.</param>
/// <param name="ContactPhone">The number customers and couriers call.</param>
/// <param name="ContactEmail">The address customer correspondence comes from.</param>
/// <param name="GstRegistrationReference">Which GST registration the branch trades under.</param>
/// <param name="Reason">Why the change is being made.</param>
public sealed record OpenBranchPayload(
    string? Code,
    string? Name,
    string? TimeZoneId,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? ContactPhone,
    string? ContactEmail,
    string? GstRegistrationReference,
    string? Reason)
{
    /// <summary>The editable description this request carries.</summary>
    public BranchDetails Details() => new(
        Name,
        TimeZoneId,
        AddressLine1,
        AddressLine2,
        City,
        State,
        PostalCode,
        ContactPhone,
        ContactEmail,
        GstRegistrationReference);
}

/// <summary>One feature flag or module toggle.</summary>
/// <param name="Key">The flag key. A module toggle's key is <c>module.</c> and the module code.</param>
/// <param name="Enabled">Whether it is on for the organisation.</param>
/// <param name="Reason">Why it was last changed.</param>
/// <param name="UpdatedAt">When it was last changed.</param>
/// <param name="UpdatedBy">Who last changed it, or null for a value that arrived with the seed data.</param>
/// <param name="Revision">How many times it has been set.</param>
/// <param name="PropagationSeconds">
/// How long every other node may take to see this value. Stated so an administrator who has just moved
/// a toggle knows why the till has not changed yet, rather than moving it again.
/// </param>
/// <param name="Version">The version an edit must present in <c>If-Match</c>.</param>
public sealed record FeatureFlagPayload(
    string Key,
    bool Enabled,
    string? Reason,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy,
    int Revision,
    int PropagationSeconds,
    string Version)
{
    /// <summary>Projects a flag onto the wire.</summary>
    /// <param name="flag">The flag.</param>
    public static FeatureFlagPayload From(AdministeredFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        return new FeatureFlagPayload(
            flag.Key,
            flag.Enabled,
            flag.Reason,
            flag.UpdatedAt,
            flag.UpdatedBy,
            flag.Revision,
            FeatureFlagPropagation.DefaultSeconds,
            flag.Version.Version);
    }
}

/// <summary>What a flag should be set to, and why.</summary>
/// <param name="Enabled">Whether the flag should be on.</param>
/// <param name="Reason">Why the change is being made.</param>
public sealed record SetFeatureFlagPayload(bool Enabled, string? Reason);

/// <summary>The new description of a branch that already exists.</summary>
/// <remarks>
/// No code. It cannot change, and publishing a field that is accepted and ignored would invite a
/// client to send one and believe it took effect.
/// </remarks>
/// <param name="Name">The branch name.</param>
/// <param name="TimeZoneId">The IANA timezone its due dates are computed in.</param>
/// <param name="AddressLine1">First line of the street address.</param>
/// <param name="AddressLine2">Second line of the street address.</param>
/// <param name="City">The town or city.</param>
/// <param name="State">The state.</param>
/// <param name="PostalCode">The postal code.</param>
/// <param name="ContactPhone">The number customers and couriers call.</param>
/// <param name="ContactEmail">The address customer correspondence comes from.</param>
/// <param name="GstRegistrationReference">Which GST registration the branch trades under.</param>
/// <param name="Reason">Why the change is being made.</param>
public sealed record ReconfigureBranchPayload(
    string? Name,
    string? TimeZoneId,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? ContactPhone,
    string? ContactEmail,
    string? GstRegistrationReference,
    string? Reason)
{
    /// <summary>The editable description this request carries.</summary>
    public BranchDetails Details() => new(
        Name,
        TimeZoneId,
        AddressLine1,
        AddressLine2,
        City,
        State,
        PostalCode,
        ContactPhone,
        ContactEmail,
        GstRegistrationReference);
}

/// <summary>One page of the audit trail.</summary>
/// <param name="Entries">The entries, newest first.</param>
/// <param name="NextCursor">Pass this back as <c>cursor</c> for the next page, or null at the end.</param>
public sealed record AuditPagePayload(IReadOnlyList<AuditEntryPayload> Entries, string? NextCursor)
{
    /// <summary>Projects a page onto the wire.</summary>
    /// <param name="page">The page.</param>
    public static AuditPagePayload From(AuditPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new AuditPagePayload(
            [.. page.Entries.Select(AuditEntryPayload.From)],
            page.NextCursor);
    }
}

/// <summary>One audit entry as a reviewer reads it.</summary>
/// <param name="Sequence">Its position in the chain, which is what an operator quotes.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Action">The stable dotted action name.</param>
/// <param name="EntityType">The kind of thing it was about.</param>
/// <param name="EntityId">Which one.</param>
/// <param name="ActorId">Who acted, or null for the system.</param>
/// <param name="ActorDisplayName">Their name, as it was at the time.</param>
/// <param name="BranchId">The branch the action was taken in, where there was one.</param>
/// <param name="CorrelationId">The request it belonged to, for joining it to logs and traces.</param>
/// <param name="Reason">The reason the actor gave, where the action demanded one.</param>
/// <param name="Summary">What happened, in words.</param>
/// <param name="Before">Redacted prior state as JSON, or null for a creation.</param>
/// <param name="After">Redacted resulting state as JSON, or null for a deletion.</param>
public sealed record AuditEntryPayload(
    long Sequence,
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    Guid EntityId,
    Guid? ActorId,
    string ActorDisplayName,
    Guid? BranchId,
    string? CorrelationId,
    string? Reason,
    string Summary,
    string? Before,
    string? After)
{
    /// <summary>Projects one entry onto the wire.</summary>
    /// <param name="entry">The entry.</param>
    public static AuditEntryPayload From(AuditRecord entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new AuditEntryPayload(
            entry.Sequence,
            entry.OccurredAt,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.ActorId,
            entry.ActorDisplayName,
            entry.BranchId,
            entry.CorrelationId,
            entry.Reason,
            entry.Summary,
            entry.Before,
            entry.After);
    }
}

/// <summary>The slice of the trail to export, and why.</summary>
/// <param name="EntityType">Only entries about this kind of thing.</param>
/// <param name="EntityId">Only entries about this one.</param>
/// <param name="ActorId">Only entries recorded against this actor.</param>
/// <param name="Action">Only actions starting with this.</param>
/// <param name="From">Only entries at or after this instant.</param>
/// <param name="To">Only entries strictly before this instant.</param>
/// <param name="Cursor">Where to continue from, for an export taken in parts.</param>
/// <param name="Limit">How many entries to return.</param>
/// <param name="Reason">Why the export is being taken. Recorded in the trail with the exporter's name.</param>
public sealed record ExportAuditPayload(
    string? EntityType,
    Guid? EntityId,
    Guid? ActorId,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Limit,
    string? Reason);
