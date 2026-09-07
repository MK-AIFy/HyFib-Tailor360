using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// Arranges the person at a counter: an account assigned to one branch, holding some of the customer
/// permissions and nothing else, signed in.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <c>AdministrationHarness</c>. That one grants
/// <c>admin.organisation.read_all_branches</c> to every account it makes, because every endpoint it
/// was written for is organisation-scoped — and an account holding organisation reach would pass the
/// branch tests below whether or not the branch rules worked. What these tests need is the opposite:
/// a role whose reach is its holder's branches, so that <c>assigned-branches</c> is what the endpoint
/// is actually being asked about.
/// </para>
/// <para>
/// No second factor is answered either, and that is a fact about the catalogue rather than a shortcut:
/// none of <c>customers.read</c>, <c>customers.create</c>, <c>customers.update</c> or
/// <c>customers.deactivate</c> is flagged for multi-factor or for step-up. A harness that enrolled
/// anyway would hide the day one of them gained a flag nobody meant to add.
/// </para>
/// </remarks>
internal static class CustomerHarness
{
    /// <summary>
    /// An organisation this deployment does not serve, for the one check no endpoint can reach.
    /// </summary>
    private static readonly Guid OtherOrganisationId =
        Guid.Parse("0199c000-0000-7000-8000-0000000000bb");

    /// <summary>The permissions a Reception account needs to work through the whole record.</summary>
    public static string[] Reception { get; } =
    [
        CustomersPermissions.Read,
        CustomersPermissions.ReadContact,
        CustomersPermissions.Create,
        CustomersPermissions.Update,
        CustomersPermissions.Deactivate,
    ];

    /// <summary>Opens a branch if this run has not opened it yet.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="branchId">The branch identifier the tests use.</param>
    /// <param name="code">The branch code, which becomes part of every customer number it allocates.</param>
    /// <returns>The branch code, for convenience at the call site.</returns>
    public static async Task<string> BranchAsync(WebApplicationFixture fixture, Guid branchId, string code)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        if (!await context.Branches.AnyAsync(
                branch => branch.Id == branchId, TestContext.Current.CancellationToken))
        {
            context.Branches.Add(Branch.Open(
                branchId,
                SessionTestData.OrganisationId,
                code,
                $"Customer test branch {code}",
                clock.UtcNow).Value);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return code;
    }

    /// <summary>
    /// Writes a customer record belonging to a different organisation, straight into the schema.
    /// </summary>
    /// <remarks>
    /// There is no way to make one through the API, and that is the point: a session carries the
    /// organisation it acts in, so every record created through an endpoint belongs to this one. The
    /// handler's organisation check is therefore unreachable from the outside, and a test that only
    /// asked about identifiers matching nothing would pass against an implementation that had no such
    /// check at all.
    /// </remarks>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="owningBranchId">The branch to record as the owner. Never read by the check under test.</param>
    /// <returns>The identifier of a record this organisation must never acknowledge.</returns>
    public static async Task<Guid> CustomerOfAnotherOrganisationAsync(
        WebApplicationFixture fixture,
        Guid owningBranchId)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

        var details = CustomerDetails.Create(
            "Kavitha elsewhere",
            null,
            "+919000445566",
            null,
            "elsewhere.demo@example.invalid",
            null,
            "Peelamedu",
            "641004",
            "ta-IN");

        details.IsSuccess.ShouldBeTrue();

        var customer = Customer.Register(
            ids.NewId(),
            OtherOrganisationId,
            $"C-OTHER-{AdministrationHarness.UniqueToken(6)}",
            owningBranchId,
            details.Value,
            clock.UtcNow).Value;

        context.Customers.Add(customer);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return customer.Id;
    }

    /// <summary>Creates an account at one branch, grants it exactly the permissions named, and signs it in.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="prefix">A short, readable prefix so a row can be traced back to its test.</param>
    /// <param name="clientAddress">The address the requests appear to come from.</param>
    /// <param name="branchId">The branch the account works in, which becomes its session's active branch.</param>
    /// <param name="permissions">The permission keys to grant. An empty set is a signed-in caller holding nothing.</param>
    /// <returns>A signed-in client.</returns>
    public static async Task<AuthenticationClient> CounterAsync(
        WebApplicationFixture fixture,
        string prefix,
        string clientAddress,
        Guid branchId,
        params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(permissions);

        var userName = $"{prefix}-{Guid.CreateVersion7():n}"[..Math.Min(prefix.Length + 13, 40)];

        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var hashing = scope.ServiceProvider.GetRequiredService<IPasswordHashingService>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var now = clock.UtcNow;

            var user = StaffUser.Invite(
                ids.NewId(),
                SessionTestData.OrganisationId,
                userName,
                $"{userName}@synthetic.invalid",
                $"Counter {userName}",
                now,
                branchId).Value;

            user.SetPassword(
                    ids.NewId(),
                    hashing.Hash(user, AuthenticationTestData.Password),
                    hashing.AlgorithmName,
                    now,
                    by: null)
                .IsSuccess.ShouldBeTrue();

            context.Users.Add(user);

            var key = new string([.. prefix.Where(char.IsAsciiLetterLower)]);
            var role = Role.Define(
                ids.NewId(),
                SessionTestData.OrganisationId,
                $"{key}_{AdministrationHarness.UniqueToken(12)}"[..Math.Min(key.Length + 13, 30)],
                $"Counter {prefix}",
                "A branch-reach role created for one test.",
                RoleReach.Branch,
                now).Value;

            foreach (var permission in permissions)
            {
                role.Grant(permission, now, by: null).IsSuccess.ShouldBeTrue();
            }

            context.Roles.Add(role);
            context.UserRoles.Add(UserRoleAssignment.Create(user.Id, role.Id, now));
            context.UserBranchAssignments.Add(
                UserBranchAssignment.Create(user.Id, branchId, now, isPrimary: true));

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var client = AuthenticationClient.Open(fixture, clientAddress);

        (await client.PostAsync(
                "/api/v1/auth/login",
                new { identifier = userName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        return client;
    }
}
