using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Application.Abstractions;

/// <summary>
/// Finding staff accounts for an administrative list.
/// </summary>
/// <remarks>
/// Separate from the account store because the question is different: that one loads a whole aggregate
/// to decide something about it, and this one reads a page of summaries to put on a screen. Loading
/// aggregates to render a list would fetch every password, authenticator and recovery code in the
/// organisation to show a table of names.
/// </remarks>
public interface IStaffDirectory
{
    /// <summary>Reads one page of accounts.</summary>
    /// <param name="query">What to look for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StaffPage> SearchAsync(StaffQuery query, CancellationToken cancellationToken = default);
}

/// <summary>What an administrator is looking for.</summary>
/// <remarks>
/// <para>
/// <b>The free-text term searches names, never contact details.</b> Searching by email or phone number
/// would turn this endpoint into a way of confirming whether an address belongs to a member of staff,
/// which is a question an administrator can already answer by opening the record and an attacker
/// should not be able to ask at all. The term is never logged either, for the same reason.
/// </para>
/// </remarks>
/// <param name="OrganisationId">The organisation whose staff are being listed.</param>
/// <param name="Status">Only accounts in this state, or every state when null.</param>
/// <param name="RoleKey">Only accounts holding this role, or every account when null.</param>
/// <param name="BranchId">Only accounts assigned to this branch, or every account when null.</param>
/// <param name="Search">A fragment of a display name or a sign-in name.</param>
/// <param name="Cursor">Where to continue from, or null for the first page.</param>
/// <param name="Limit">How many accounts to return.</param>
public sealed record StaffQuery(
    Guid OrganisationId,
    UserStatus? Status = null,
    string? RoleKey = null,
    Guid? BranchId = null,
    string? Search = null,
    string? Cursor = null,
    int Limit = StaffQuery.DefaultLimit)
{
    /// <summary>The page size when the caller does not choose one.</summary>
    public const int DefaultLimit = 25;

    /// <summary>
    /// The largest page this endpoint will return.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than a suggestion: an administrative list is a screen, and a caller asking for
    /// ten thousand accounts in one response is either building an export — which is its own audited
    /// endpoint — or is not a screen at all.
    /// </remarks>
    public const int MaximumLimit = 100;
}

/// <summary>One page of accounts, and where to continue from.</summary>
/// <param name="Users">The accounts on this page, oldest first.</param>
/// <param name="NextCursor">
/// The cursor for the following page, or null when this is the last one. Keyset rather than an offset,
/// so a page cannot skip or repeat an account because somebody was invited while the reader was paging.
/// </param>
public sealed record StaffPage(IReadOnlyList<StaffSummary> Users, string? NextCursor);

/// <summary>One account as an administrative list shows it.</summary>
/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The name shown on screen.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="Status">Whether it is invited, active, suspended or closed.</param>
/// <param name="MfaEnrolment">Whether a second factor is enrolled.</param>
/// <param name="HomeBranchId">Its default branch.</param>
/// <param name="RoleKeys">The roles it holds, by key.</param>
/// <param name="LastSignInAt">When it was last used, which is what marks it dormant.</param>
/// <param name="CreatedAt">When it was created.</param>
public sealed record StaffSummary(
    Guid UserId,
    string DisplayName,
    string UserName,
    UserStatus Status,
    MfaEnrolmentState MfaEnrolment,
    Guid? HomeBranchId,
    IReadOnlyList<string> RoleKeys,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt);
