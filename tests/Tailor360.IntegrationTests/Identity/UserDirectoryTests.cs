using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// Identity's published staff directory, which #33, #44 and #45 will read instead of the table.
/// </summary>
/// <remarks>
/// What is worth asserting about a read contract is not that it returns rows — it is that it returns
/// the right ones and only the fields it promised. A directory that quietly carried a phone number, or
/// that offered a suspended account as an assignee, would be found by its consumers rather than here.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UserDirectoryTests(WebApplicationFixture fixture)
{
    /// <summary>Whether a PostgreSQL instance was found for this run.</summary>
    public static bool Available => DatabaseAvailability.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserDirectoryTests))]
    public async Task AMemberOfStaffCarriesTheirName_Roles_BranchesAndLocale()
    {
        var (user, roleId) = await AdministrationHarness.AccountAsync(
            fixture, "dir-one", grantPermission: null);

        var roleKey = await RoleKeyAsync(roleId);

        var found = (await DirectoryAsync(directory => directory.FindAsync(
            user.Id, TestContext.Current.CancellationToken))).ShouldNotBeNull();

        found.UserId.ShouldBe(user.Id);
        found.DisplayName.ShouldBe(user.DisplayName);
        found.IsActive.ShouldBeTrue();
        found.RoleKeys.ShouldContain(roleKey);
        found.BranchIds.ShouldContain(SessionTestData.HomeBranchId);

        // The account has no preferences row until somebody changes a preference, which is a real state
        // rather than a gap — so the contract answers with the default rather than with null.
        found.Locale.ShouldBe(UserPreferences.DefaultLocale);

        (await DirectoryAsync(directory => directory.FindAsync(
            Guid.CreateVersion7(), TestContext.Current.CancellationToken))).ShouldBeNull();
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserDirectoryTests))]
    public async Task TheDirectoryCarriesNoContactDetails()
    {
        var (user, _) = await AdministrationHarness.AccountAsync(
            fixture, "dir-private", grantPermission: null);

        var found = (await DirectoryAsync(directory => directory.FindAsync(
            user.Id, TestContext.Current.CancellationToken))).ShouldNotBeNull();

        // Read from the record's own shape rather than from a list somebody has to remember to update:
        // adding a contact field to StaffMember is exactly the change this is here to catch.
        var published = typeof(StaffMember)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        published.ShouldBe(
            [
                nameof(StaffMember.UserId),
                nameof(StaffMember.DisplayName),
                nameof(StaffMember.IsActive),
                nameof(StaffMember.Locale),
                nameof(StaffMember.RoleKeys),
                nameof(StaffMember.BranchIds),
                nameof(StaffMember.HomeBranchId),
            ],
            ignoreOrder: true,
            "a module that needs a member of staff's phone number needs a decision, not a wider contract");

        // And the values are what the module has, not a redaction of something wider: the name is real,
        // and the address the account signs in with appears nowhere in the answer.
        found.DisplayName.ShouldBe(user.DisplayName);
        System.Text.Json.JsonSerializer.Serialize(found)
            .ShouldNotContain(user.Email, Case.Insensitive);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserDirectoryTests))]
    public async Task OnlyPeopleWhoCanSignInAreOfferedAsABranchsStaff()
    {
        var branchId = await BranchAsync("DIR1");

        var active = await AssignedAccountAsync("dir-active", branchId, UserStatus.Active);
        var suspended = await AssignedAccountAsync("dir-susp", branchId, UserStatus.Suspended);
        var invited = await AssignedAccountAsync("dir-inv", branchId, UserStatus.Invited);
        var elsewhere = await AssignedAccountAsync(
            "dir-else", await BranchAsync("DIR2"), UserStatus.Active);

        var staff = await DirectoryAsync(directory => directory.ListActiveInBranchAsync(
            branchId, TestContext.Current.CancellationToken));

        var found = staff.Select(member => member.UserId).ToArray();

        found.ShouldContain(active);

        // Offering any of these three as an assignee would produce a job nobody can pick up: two of them
        // cannot sign in at all, and the third does not work here.
        found.ShouldNotContain(suspended);
        found.ShouldNotContain(invited);
        found.ShouldNotContain(elsewhere);

        staff.Select(member => member.DisplayName)
            .ShouldBe([.. staff.Select(member => member.DisplayName).Order(StringComparer.Ordinal)],
                "a workload board reads in name order, and the order is the contract's to fix");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(UserDirectoryTests))]
    public async Task ManyAreReadInOneQueryAndAnIdentifierMatchingNobodyIsSkipped()
    {
        var branchId = await BranchAsync("DIR3");

        var first = await AssignedAccountAsync("dir-many1", branchId, UserStatus.Active);
        var second = await AssignedAccountAsync("dir-many2", branchId, UserStatus.Suspended);
        var gone = Guid.CreateVersion7();

        var found = await DirectoryAsync(directory => directory.FindManyAsync(
            [first, second, gone, first], TestContext.Current.CancellationToken));

        found.Select(member => member.UserId).ShouldBe([first, second], ignoreOrder: true);

        // Unlike the branch list, this one answers about anybody named — a report showing who did what
        // last month has to name the person who has since been suspended.
        found.First(member => member.UserId == second).IsActive.ShouldBeFalse();

        // A recipient whose account was deleted should not stop the report reaching the others.
        (await DirectoryAsync(directory => directory.FindManyAsync(
            [gone], TestContext.Current.CancellationToken))).ShouldBeEmpty();

        (await DirectoryAsync(directory => directory.FindManyAsync(
            [], TestContext.Current.CancellationToken))).ShouldBeEmpty();
    }

    private async Task<T> DirectoryAsync<T>(Func<IUserDirectory, Task<T>> read)
    {
        using var scope = fixture.Services.CreateScope();

        return await read(scope.ServiceProvider.GetRequiredService<IUserDirectory>());
    }

    private async Task<string> RoleKeyAsync(Guid roleId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await context.Roles
            .AsNoTracking()
            .Where(role => role.Id == roleId)
            .Select(role => role.Key)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> BranchAsync(string code)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

        var unique = $"{code}{AdministrationHarness.UniqueToken(4)}".ToUpperInvariant();

        var branch = Branch.Open(
            ids.NewId(), SessionTestData.OrganisationId, unique, $"Directory test {unique}",
            clock.UtcNow).Value;

        context.Branches.Add(branch);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return branch.Id;
    }

    private async Task<Guid> AssignedAccountAsync(string prefix, Guid branchId, UserStatus status)
    {
        var (user, _) = await AdministrationHarness.AccountAsync(fixture, prefix, grantPermission: null);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;

        var tracked = await context.Users.SingleAsync(
            candidate => candidate.Id == user.Id, TestContext.Current.CancellationToken);

        switch (status)
        {
            case UserStatus.Suspended:
                tracked.Suspend(now, by: null).IsSuccess.ShouldBeTrue();
                break;
            case UserStatus.Invited:
                // Reactivating a closed account reopens it as an invitation: the password and every
                // second factor are cleared, so the person returning proves who they are from the
                // beginning. It is the only route back to Invited, which is why the test takes it.
                tracked.Deactivate(now, by: null).IsSuccess.ShouldBeTrue();
                tracked.Reactivate(now, Guid.CreateVersion7(), "Rejoined for one test.")
                    .IsSuccess.ShouldBeTrue();
                break;
            default:
                break;
        }

        context.UserBranchAssignments.Add(
            UserBranchAssignment.Create(user.Id, branchId, now, isPrimary: false));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        tracked.Status.ShouldBe(status);

        return user.Id;
    }
}
