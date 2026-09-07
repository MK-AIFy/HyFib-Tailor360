using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Administration;

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
