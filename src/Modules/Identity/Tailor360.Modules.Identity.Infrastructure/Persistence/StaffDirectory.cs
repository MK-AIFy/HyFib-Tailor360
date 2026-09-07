using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Application.Abstractions;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Reads pages of staff accounts for the administration list.
/// </summary>
/// <param name="context">The module's context.</param>
public sealed class StaffDirectory(IdentityDbContext context) : IStaffDirectory
{
    /// <inheritdoc />
    public async Task<StaffPage> SearchAsync(
        StaffQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, StaffQuery.MaximumLimit);
        var accounts = context.Users.AsNoTracking().Where(user => user.OrganisationId == query.OrganisationId);

        if (query.Status is { } status)
        {
            accounts = accounts.Where(user => user.Status == status);
        }

        if (query.RoleKey is { Length: > 0 } roleKey)
        {
            accounts = accounts.Where(user => context.UserRoles
                .Where(assignment => assignment.UserId == user.Id)
                .Join(context.Roles, assignment => assignment.RoleId, role => role.Id, (_, role) => role.Key)
                .Any(key => key == roleKey));
        }

        if (query.BranchId is { } branchId)
        {
            accounts = accounts.Where(user => context.UserBranchAssignments
                .Any(assignment => assignment.UserId == user.Id && assignment.BranchId == branchId));
        }

        if (query.Search is { Length: > 0 } term)
        {
            // Names only. An administrator looking for somebody knows their name; matching an address
            // here would answer "does this email belong to a member of staff" for anybody who reached
            // the endpoint.
            var pattern = $"%{term.Trim()}%";

            accounts = accounts.Where(user =>
                EF.Functions.ILike(user.DisplayName, pattern) || EF.Functions.ILike(user.UserName, pattern));
        }

        if (Decode(query.Cursor) is var (createdAt, lastId) && lastId != Guid.Empty)
        {
            // Keyset, so a page cannot skip or repeat an account because somebody was invited between
            // one request and the next. The identifier breaks ties, and it has to, because two accounts
            // created in the same microsecond are ordinary in a seeding run.
            accounts = accounts.Where(user =>
                user.CreatedAt > createdAt || (user.CreatedAt == createdAt && user.Id.CompareTo(lastId) > 0));
        }

        var page = await accounts
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .Take(limit + 1)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.UserName,
                user.Status,
                user.MfaEnrolment,
                user.HomeBranchId,
                user.LastSignInAt,
                user.CreatedAt,
                RoleKeys = context.UserRoles
                    .Where(assignment => assignment.UserId == user.Id)
                    .Join(context.Roles, assignment => assignment.RoleId, role => role.Id, (_, role) => role.Key)
                    .OrderBy(key => key)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        // One more than asked for, so "is there another page" is answered without a second count query
        // that could disagree with the page it describes.
        var hasMore = page.Count > limit;
        var users = page.Take(limit).ToList();

        return new StaffPage(
            [.. users.Select(user => new StaffSummary(
                user.Id,
                user.DisplayName,
                user.UserName,
                user.Status,
                user.MfaEnrolment,
                user.HomeBranchId,
                user.RoleKeys,
                user.LastSignInAt,
                user.CreatedAt))],
            hasMore && users.Count > 0 ? Encode(users[^1].CreatedAt, users[^1].Id) : null);
    }

    /// <summary>
    /// Encodes where a page ended.
    /// </summary>
    /// <remarks>
    /// The instant is written as a round-trip string rather than as ticks, because the column stores
    /// microseconds and a tick-precision cursor would name an instant no row can equal — so the
    /// tie-break on the identifier would never fire and a page boundary that fell between two accounts
    /// created in the same microsecond would drop one of them.
    /// </remarks>
    private static string Encode(DateTimeOffset createdAt, Guid id)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{createdAt.ToString("O", CultureInfo.InvariantCulture)}|{id}"));

    private static (DateTimeOffset CreatedAt, Guid Id) Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (default, Guid.Empty);
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');

            return parts.Length == 2
                   && DateTimeOffset.TryParse(
                       parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt)
                   && Guid.TryParse(parts[1], out var id)
                ? (createdAt, id)
                : (default, Guid.Empty);
        }
        catch (FormatException)
        {
            // A cursor that is not ours is treated as no cursor: the caller gets the first page rather
            // than an error, because the only way to hold a malformed one is to have edited a URL.
            return (default, Guid.Empty);
        }
    }
}
