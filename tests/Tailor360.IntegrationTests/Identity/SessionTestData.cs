using Shouldly;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Modules.Identity.Infrastructure.Persistence;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>Seeds the one thing the session tests need: an account a session can belong to.</summary>
/// <remarks>
/// Synthetic throughout. No name, address or number here belongs to anyone; a fixture carrying real
/// personal data would be a data-protection incident waiting for someone to copy it into a bug report.
/// </remarks>
public static class SessionTestData
{
    /// <summary>The organisation every seeded account belongs to.</summary>
    public static readonly Guid OrganisationId = Guid.Parse("0199c000-0000-7000-8000-0000000000aa");

    /// <summary>The branch seeded accounts call home.</summary>
    public static readonly Guid HomeBranchId = Guid.Parse("0199c000-0000-7000-8000-00000000000a");

    /// <summary>A password hash of the right shape. Never a real hash of a real password.</summary>
    public const string EncodedHash =
        "$argon2id$v=19$m=19456,t=2,p=1$c3ludGhldGljc2FsdA$c3ludGhldGljaGFzaHZhbHVlZm9ydGVzdHM";

    /// <summary>Creates an active account and saves it.</summary>
    public static async Task<StaffUser> CreateActiveUserAsync(
        IdentityDbContext context,
        DateTimeOffset now,
        string userName = "meena",
        string displayName = "Meena R",
        Guid? homeBranchId = null)
    {
        var user = StaffUser.Invite(
            Guid.CreateVersion7(),
            OrganisationId,
            userName,
            $"{userName}@synthetic.invalid",
            displayName,
            now,
            homeBranchId ?? HomeBranchId).Value;

        // An invited account becomes active the moment it has a password, which is the state every
        // session test starts from.
        user.SetPassword(Guid.CreateVersion7(), EncodedHash, "argon2id", now, by: null).IsSuccess
            .ShouldBeTrue();

        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return user;
    }
}
