using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OtpNet;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Preferences;
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
        CustomersPermissions.ReadConsent,
        CustomersPermissions.Create,
        CustomersPermissions.Update,
        CustomersPermissions.Deactivate,
    ];

    /// <summary>
    /// What a Branch Manager holds: everything Reception does, plus the merge.
    /// </summary>
    /// <remarks>
    /// <c>docs/prd/raci.md</c> note (1): "the plan's proposal is that the Branch Manager holds it, so
    /// Reception is consulted-then-blocked rather than free to merge". A client built with these
    /// permissions still cannot merge unless it was opened through <see cref="ManagerAsync"/>, because
    /// <c>customers.merge</c> also demands a second factor and a fresh re-authentication.
    /// </remarks>
    public static string[] BranchManager { get; } = [.. Reception, CustomersPermissions.Merge];

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

    /// <summary>
    /// Writes a customer record belonging to this organisation, straight into the schema.
    /// </summary>
    /// <remarks>
    /// The published queries are the thing under test, so the record they read is arranged directly
    /// rather than through the endpoints. Going through the API would work and would also make every
    /// one of those tests fail whenever the registration endpoint changed, which is a coupling worth
    /// not having: the endpoints have their own tests, and these ask about the contract.
    /// </remarks>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="owningBranchId">The branch to record as the owner.</param>
    /// <param name="language">
    /// The language on the record, which is what an unrecorded preference falls back to.
    /// </param>
    /// <returns>The identifier of the record.</returns>
    /// <summary>
    /// A signed-in Branch Manager whose session is fresh enough for a step-up endpoint.
    /// </summary>
    /// <param name="fixture">The host.</param>
    /// <param name="prefix">A short prefix, which becomes part of the account and role names.</param>
    /// <param name="clientAddress">The client address, so rate limits do not bleed between tests.</param>
    /// <param name="branchId">The branch the manager is assigned to.</param>
    /// <param name="permissions">What the role grants.</param>
    /// <returns>The client, signed in with a second factor enrolled.</returns>
    public static async Task<AuthenticationClient> ManagerAsync(
        WebApplicationFixture fixture,
        string prefix,
        string clientAddress,
        Guid branchId,
        params string[] permissions)
    {
        var client = await CounterAsync(fixture, prefix, clientAddress, branchId, permissions);

        // Real TOTP, not a stub. customers.merge is declared RequiresMfa and RequiresStepUp, and a
        // session that reached step-up freshness by any other route would not be the session the
        // endpoint actually sees — which is the whole point of testing it through the host.
        var started = await client.PostAsync("/api/v1/auth/mfa/enrol");
        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        var enrolment = (await AuthenticationClient.ReadAsync<Enrolment>(started)).ShouldNotBeNull();

        var secret = Base32Encoding.ToBytes(
            enrolment.ManualEntryKey.Replace(" ", string.Empty, StringComparison.Ordinal));
        var code = new Totp(secret, enrolment.PeriodSeconds, totpSize: enrolment.Digits).ComputeTotp();

        (await client.PostAsync("/api/v1/auth/mfa/enrol/confirm", new { code }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        return client;
    }

    public static async Task<Guid> CustomerAsync(
        WebApplicationFixture fixture,
        Guid owningBranchId,
        string language = "en-IN")
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

        var token = AdministrationHarness.UniqueToken(6);

        var details = CustomerDetails.Create(
            $"Contract subject {token}",
            "\u0BAE\u0BC0\u0BA9\u0BBE",
            UniquePhone(),
            null,
            $"contract.{token}@example.invalid",
            "12 Trichy Road",
            "Ramanathapuram",
            "641045",
            language);

        details.IsSuccess.ShouldBeTrue();

        var customer = Customer.Register(
            ids.NewId(),
            SessionTestData.OrganisationId,
            $"C-CONTRACT-{token}",
            owningBranchId,
            details.Value,
            clock.UtcNow).Value;

        context.Customers.Add(customer);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return customer.Id;
    }

    /// <summary>
    /// A telephone number nothing else in the suite, or any run before it, has ever used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Synthetic and obviously so — <c>+91 90 00 …</c> — with a tail that is <strong>counted, not
    /// drawn</strong>. What this replaced reasoned about the whole ten-digit number: a random four-digit
    /// run prefix plus a per-run counter, "collide only by drawing the same four-digit prefix, one pair
    /// in ten thousand". That was true of the whole number and false of the six digits several tests
    /// actually search on — <c>docs/security/permission-matrix.md</c> documents the tail search, and
    /// <c>CustomerEndpointTests</c> asserts against it — because only two of the four prefix digits
    /// reached the tail. Two runs shared tail space whenever their prefixes agreed in their last two
    /// digits: one pair in a hundred, which is what made
    /// <c>CustomerEndpointTests.AWithdrawnRecordLeavesOrdinarySearchAndComesBackWhenItIsRestored</c> and
    /// its neighbour fail on an unrelated run's leftover customer (issue #125).
    /// </para>
    /// <para>
    /// No narrower random draw fixes this — the arithmetic does not allow it, because the tail has only
    /// a million values and one run already uses a few hundred of them, so any scheme that draws a
    /// random run identity and leaves the rest to a per-run counter trades within-run uniqueness against
    /// across-run uniqueness. The fix removes the draw instead of shrinking it: once per process, the
    /// starting point is read from the database as the highest tail any earlier run already claimed —
    /// through <see cref="PhoneSequenceSeed"/> — and every call after that takes the next integer.
    /// That is collision-free within a run by construction, and against every run before it because the
    /// sequence only ever counts up from what they left behind.
    /// </para>
    /// </remarks>
    /// <returns>The number, in E.164.</returns>
    public static string UniquePhone()
    {
        var sequence = PhoneSequenceSeed.Value + Interlocked.Increment(ref _phoneSequence);

        // The tail search this whole scheme depends on has exactly a million values. Silently letting
        // the sum spill past it would print a seventh digit and reset the tail to a range earlier runs
        // already used — the exact wraparound this fix exists to remove. Fail loudly instead: a test
        // database this full needs resetting, not a scheme that pretends it still has room.
        if (sequence > 999_999)
        {
            throw new InvalidOperationException(
                $"CustomerHarness.UniquePhone has exhausted its six-digit tail space (reached {sequence}). "
                + "Reset the test database (./scripts/dev reset) rather than continuing to draw numbers "
                + "past it — a wrapped tail would collide with numbers earlier runs already claimed.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"+919000{sequence:D6}");
    }

    /// <summary>
    /// The highest tail any earlier run already claimed, read once per process from the same connection
    /// string the integration tier resolves for itself (<see cref="DatabaseAvailability.ConnectionString"/>).
    /// </summary>
    /// <remarks>
    /// Zero when no database is reachable — a fixture that reaches this far without one is already
    /// failing elsewhere, so falling back to zero here does not hide anything, and it keeps the seed a
    /// pure function of what the database actually holds rather than of process start-up order.
    /// </remarks>
    private static readonly Lazy<int> PhoneSequenceSeed = new(ReadPhoneSequenceSeed);

    private static int _phoneSequence;

    private static int ReadPhoneSequenceSeed()
    {
        if (DatabaseAvailability.ConnectionString is not { } connectionString)
        {
            return 0;
        }

        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using var command = new NpgsqlCommand(
            """
            select coalesce(max(right(phone_e164, 6)::int), 0)
            from customers.customers
            where phone_e164 like '+9190%'
            """,
            connection);

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Defines a consent purpose of this test's own, and optionally publishes a wording for it.
    /// </summary>
    /// <remarks>
    /// A purpose per test rather than the five <c>init-reference-data</c> seeds, because a purpose is
    /// organisation-wide: publishing a wording bumps a version every other test would then be asserting
    /// against, and retiring one is not undone by re-running the seeder. Keys are lower-case hexadecimal
    /// with an underscore, which is what the key rule allows.
    /// </remarks>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="withWording">Whether to publish a first wording, which is what lets it be answered.</param>
    /// <param name="retired">Whether to retire it immediately.</param>
    /// <returns>The purpose's key.</returns>
    public static async Task<string> PurposeAsync(
        WebApplicationFixture fixture,
        bool withWording = true,
        bool retired = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

        var key = $"test_{AdministrationHarness.UniqueToken(12)}";

        var purpose = ConsentPurpose.Define(
            ids.NewId(),
            SessionTestData.OrganisationId,
            key,
            $"Test purpose {key}",
            "Defined by an integration test. Nothing is ever sent on the strength of it.",
            clock.UtcNow);

        purpose.IsSuccess.ShouldBeTrue();

        if (withWording)
        {
            // Deliberately not the words of any real notice. What matters to these tests is that a
            // version exists for a record to name, not what it says.
            purpose.Value.PublishWording(
                ids.NewId(),
                "Synthetic wording for an integration test.",
                clock.UtcNow)
                .IsSuccess.ShouldBeTrue();
        }

        if (retired)
        {
            purpose.Value.Retire(clock.UtcNow, null).IsSuccess.ShouldBeTrue();
        }

        context.ConsentPurposes.Add(purpose.Value);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return key;
    }

    /// <summary>Seeds the five consent purposes <c>init-reference-data</c> creates.</summary>
    /// <remarks>
    /// Idempotent, like the command itself, so a test that needs the register to exist may call it
    /// without caring whether another test already did. It publishes no wording, because the command
    /// does not either — inventing the words a customer is read is what the classification document
    /// forbids, and the refusal that follows is DC-01 enforced rather than mentioned.
    /// </remarks>
    /// <param name="fixture">The hosted application.</param>
    /// <returns>A task that completes when the register exists.</returns>
    public static async Task SeededPurposesAsync(WebApplicationFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IConsentReferenceDataSeeder>()
            .SeedConsentPurposesAsync(SessionTestData.OrganisationId, TestContext.Current.CancellationToken);
    }

    /// <summary>Appends one consent record, straight into the schema.</summary>
    /// <remarks>
    /// The table is append-only and its trigger enforces that, so a test that wants a customer to have
    /// changed her mind writes two records rather than editing one — which is exactly what the endpoint
    /// will do when it exists.
    /// </remarks>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="customerId">The customer who answered.</param>
    /// <param name="purposeKey">The purpose, by key.</param>
    /// <param name="decision">What they said.</param>
    /// <param name="recordedAt">When they said it.</param>
    /// <param name="wordingVersion">The wording version they were asked under.</param>
    /// <param name="source">How the answer reached the system.</param>
    /// <param name="recordId">The identifier to give the record, when the test needs a known one.</param>
    /// <returns>The identifier of the record written.</returns>
    public static async Task<Guid> ConsentAsync(
        WebApplicationFixture fixture,
        Guid customerId,
        string purposeKey,
        ConsentDecision decision,
        DateTimeOffset recordedAt,
        int wordingVersion = 1,
        string source = "counter, verbal",
        Guid? recordId = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();

        var record = ConsentRecord.Record(
            recordId ?? ids.NewId(),
            SessionTestData.OrganisationId,
            customerId,
            purposeKey,
            wordingVersion,
            decision,
            source,
            null,
            recordedAt,
            null);

        record.IsSuccess.ShouldBeTrue();

        context.ConsentRecords.Add(record.Value);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return record.Value.Id;
    }

    /// <summary>Records a customer's communication preference, straight into the schema.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="channels">The channels they accept. An empty set is a valid answer.</param>
    /// <param name="language">The language to write to them in, or null for the default.</param>
    /// <param name="quietHours">The window they would rather not be messaged in, or null.</param>
    /// <returns>Nothing; the row is committed.</returns>
    public static async Task PreferenceAsync(
        WebApplicationFixture fixture,
        Guid customerId,
        IEnumerable<CommunicationChannel> channels,
        string? language = null,
        QuietHours? quietHours = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var preference = CommunicationPreferences.Record(
            customerId,
            SessionTestData.OrganisationId,
            channels,
            language,
            quietHours,
            clock.UtcNow,
            null);

        preference.IsSuccess.ShouldBeTrue();

        context.CommunicationPreferences.Add(preference.Value);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed record Enrolment(string ManualEntryKey, int PeriodSeconds, int Digits);
}
