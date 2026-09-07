using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Shouldly;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// Arranges an administrator: an account holding one administrative permission, signed in, enrolled,
/// challenged and therefore freshly re-authenticated.
/// </summary>
/// <remarks>
/// Every endpoint on the administrative surface demands all four of those, so there is no cheaper
/// arrangement that reaches one. The second factor in particular has to be answered for real: a
/// session that reached step-up freshness by any other route would not be the session the endpoint
/// sees in production, and a test built on one would pass while the real path was broken.
/// </remarks>
internal static class AdministrationHarness
{
    /// <summary>Signs an administrator in and answers their second factor.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="prefix">A short, readable prefix so a row can be traced back to its test.</param>
    /// <param name="clientAddress">The address the requests appear to come from.</param>
    /// <param name="grantPermission">
    /// The administrative permission to grant, or null for a caller who is signed in and entitled to
    /// nothing — which is what proves the permission is what the endpoint is checking.
    /// </param>
    public static async Task<AdministratorClient> AdministratorAsync(
        WebApplicationFixture fixture,
        string prefix,
        string clientAddress,
        string? grantPermission = IdentityPermissions.Users)
    {
        var (user, _) = await AccountAsync(fixture, prefix, grantPermission);
        var client = AuthenticationClient.Open(fixture, clientAddress);

        (await client.PostAsync(
                "/api/v1/auth/login",
                new { identifier = user.UserName, password = AuthenticationTestData.Password }))
            .StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        var started = await client.PostAsync("/api/v1/auth/mfa/enrol");
        started.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        var enrolment = (await AuthenticationClient.ReadAsync<EnrolmentBody>(started)).ShouldNotBeNull();

        var secret = Base32Encoding.ToBytes(
            enrolment.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal));
        var code = new Totp(secret, enrolment.PeriodSeconds, totpSize: enrolment.Digits).ComputeTotp();

        (await client.PostAsync("/api/v1/auth/mfa/enrol/confirm", new { code }))
            .StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        return new AdministratorClient(client, user.Id);
    }

    /// <summary>Creates the account and the role behind an administrator, without signing in.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="prefix">A short, readable prefix.</param>
    /// <param name="grantPermission">The administrative permission to grant, or null for none.</param>
    public static async Task<(StaffUser User, Guid RoleId)> AccountAsync(
        WebApplicationFixture fixture,
        string prefix,
        string? grantPermission)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var user = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, prefix);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Time.IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<Tailor360.Platform.Abstractions.Identifiers.IIdGenerator>();
        var now = clock.UtcNow;

        // The branch assignment has a foreign key, and this fixture migrates the schema without seeding
        // reference data — so the branch is created once, by whichever test gets there first.
        if (!await context.Branches.AnyAsync(
                branch => branch.Id == SessionTestData.HomeBranchId, TestContext.Current.CancellationToken))
        {
            context.Branches.Add(Branch.Open(
                SessionTestData.HomeBranchId,
                SessionTestData.OrganisationId,
                "ADMIN1",
                "Administration test branch",
                now).Value);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Keys are lower-case letters, digits and underscores, so the prefix is folded rather than
        // used as it is written in the test name.
        var key = new string([.. prefix.Where(char.IsAsciiLetterLower)]);

        var role = Role.Define(
            ids.NewId(),
            SessionTestData.OrganisationId,
            $"{key}_role_{UniqueToken(12)}"[..Math.Min(key.Length + 18, 30)],
            $"Role {prefix}",
            "A role created for one test.",
            RoleReach.Organisation,
            now).Value;

        // Organisation reach is two facts, not one: the role is meant to see the whole organisation,
        // and its holder is granted the permission that makes that reach real. A test granting only
        // the administrative permission would be refused for a reason unrelated to what it tests.
        role.Grant(PlatformPermissions.ReadAllBranches, now, by: null).IsSuccess.ShouldBeTrue();

        if (grantPermission is { Length: > 0 })
        {
            role.Grant(grantPermission, now, by: null).IsSuccess.ShouldBeTrue();
        }

        context.Roles.Add(role);
        context.UserRoles.Add(UserRoleAssignment.Create(user.Id, role.Id, now));
        context.UserBranchAssignments.Add(
            UserBranchAssignment.Create(user.Id, SessionTestData.HomeBranchId, now, isPrimary: true));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (user, role.Id);
    }

    /// <summary>
    /// A short token that is unique across tests running at the same instant.
    /// </summary>
    /// <remarks>
    /// It takes the <em>tail</em> of the identifier, and that is the whole point. A version-7 UUID is
    /// time-ordered: its leading hex digits are a millisecond timestamp, so two tests that start in the
    /// same millisecond produce identical prefixes. Truncating from the front gives a token that looks
    /// random, collides under load, and fails a suite in a way that passes every time it is run on its
    /// own.
    /// </remarks>
    /// <param name="length">How many characters to return, at most sixteen.</param>
    public static string UniqueToken(int length = 8)
        => Guid.CreateVersion7().ToString("N")[^Math.Clamp(length, 1, 16)..];

    private sealed record EnrolmentBody(string ManualEntryKey, int PeriodSeconds, int Digits);

    /// <summary>An authenticated administrator, and the account they are.</summary>
    internal sealed class AdministratorClient(AuthenticationClient client, Guid userId) : IDisposable
    {
        /// <summary>The account this administrator is, for the self-administration guard.</summary>
        public Guid UserId { get; } = userId;

        /// <summary>Sends a read.</summary>
        public Task<HttpResponseMessage> GetAsync(string path) => client.GetAsync(path);

        /// <summary>Sends a command.</summary>
        public Task<HttpResponseMessage> PostAsync<TBody>(
            string path, TBody body, params (string Name, string Value)[] headers)
            => client.PostAsync(path, body, headers);

        /// <summary>Sends a replacement.</summary>
        public Task<HttpResponseMessage> PutAsync<TBody>(
            string path, TBody body, params (string Name, string Value)[] headers)
            => client.PutAsync(path, body, headers);

        /// <inheritdoc />
        public void Dispose() => client.Dispose();
    }
}

/// <summary>The administrative permissions these tests grant, named once.</summary>
internal static class Permissions
{
    /// <summary>Administering staff accounts.</summary>
    public const string Users = IdentityPermissions.Users;

    /// <summary>Administering the branch register.</summary>
    public const string Branches = IdentityPermissions.Branches;

    /// <summary>Changing feature flags and module toggles.</summary>
    public const string FeatureFlags = PlatformPermissions.FeatureFlags;
}
