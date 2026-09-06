using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Modules.Identity.Application.Sessions;
using Tailor360.Modules.Identity.Domain.Access;
using Tailor360.Modules.Identity.Domain.Branches;
using Tailor360.Modules.Identity.Infrastructure.Access;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Modules.Identity.Infrastructure.Sessions;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Identifiers;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Persistence.Auditing;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Persistence.Conventions;
using Tailor360.Platform.Security;
using Tailor360.Platform.Security.Audit;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Authorization;

/// <summary>
/// A running application in which every permission in the catalogue is reachable, so that the
/// owner-approved grants can be exercised as HTTP requests rather than inspected as data.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the endpoints are declared here rather than taken from a module.</b> No module publishes a
/// permissioned route yet — the matrix's <c>Built by</c> column names, for each permission, the issue
/// that will. Waiting for them would mean the role model shipped with nobody having ever seen a Tailor
/// refused a Cashier's action, and would mean the first module to publish a route would be the one
/// discovering whether any of this worked. So the routes here are one per catalogued permission, of the
/// shape every later route will have: <c>RequirePermission</c> for who the caller is, and
/// <c>ScopedToResource</c> for which branch the thing they named belongs to.
/// </para>
/// <para>
/// <b>Everything else is real.</b> The database is a real PostgreSQL instance with the real migrations;
/// the roles and grants are written by the same seeder <c>init-reference-data</c> runs; the users hold
/// real role and branch assignments; the sessions are real rows and the requests carry real cookies
/// resolved through the real ticket store. What is being asked is not "does the handler compute the
/// right answer" — the unit tier asks that — but "does a person in this role, in this branch, get this
/// answer", which is the only question the owner approving the matrix is actually asking.
/// </para>
/// <para>
/// <b>Three sessions per person.</b> One that has satisfied a second factor recently, one whose strong
/// authentication is older than the step-up window, and one that never satisfied a factor at all. They
/// are the three states the flagged permissions distinguish between, and having all three standing at
/// once means no test has to move a shared clock and put every other test in an unexpected state.
/// </para>
/// </remarks>
public sealed class AuthorisationProbeApplication : IAsyncLifetime
{
    /// <summary>The resource kind the probe routes are scoped to.</summary>
    public const string ProbeResourceKind = "authz.probe";

    /// <summary>How long before now the stale session last proved a factor.</summary>
    public static readonly TimeSpan StaleStepUpAge = TimeSpan.FromMinutes(30);

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganisationId = SessionTestData.OrganisationId;

    private readonly Dictionary<(string Role, ProbeBranch Branch, SessionStrength Strength), string> _cookies = [];
    private readonly Dictionary<string, IReadOnlySet<string>> _grants = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Role, ProbeBranch Branch), Guid> _users = [];
    private readonly Dictionary<string, string> _dualBranchCookies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _dualBranchUsers = new(StringComparer.Ordinal);

    private string? _databaseName;
    private IHost? _host;
    private HttpClient? _client;

    /// <summary>True when a PostgreSQL instance was found and the probes can run.</summary>
    public static bool IsAvailable => DatabaseAvailability.IsAvailable;

    /// <summary>The permission catalogue this application was built from.</summary>
    public PermissionCatalogue Catalogue { get; } = new([new ApplicationPermissions()]);

    /// <summary>The branch every probe user in <see cref="ProbeBranch.Own"/> is assigned to.</summary>
    public Guid OwnBranchId { get; } = Guid.Parse("0199c024-0000-7000-8000-00000000000a");

    /// <summary>A second branch, which no probe user in the first is assigned to.</summary>
    public Guid OtherBranchId { get; } = Guid.Parse("0199c024-0000-7000-8000-00000000000b");

    /// <summary>
    /// An identifier that matches no branch at all. It is what a caller editing the address bar would
    /// produce, and the answer to it has to be the same as the answer to another branch's identifier.
    /// </summary>
    public Guid UnknownBranchId { get; } = Guid.Parse("0199c024-0000-7000-8000-0000000000ff");

    /// <summary>The connection string of the database this application was built on.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>The permission keys a system role is granted by the seeder.</summary>
    public IReadOnlySet<string> GrantsOf(string roleKey)
    {
        ArgumentNullException.ThrowIfNull(roleKey);
        return _grants.TryGetValue(roleKey, out var grants)
            ? grants
            : throw new InvalidOperationException($"No system role '{roleKey}' was seeded.");
    }

    /// <summary>
    /// Asks, as one of the seeded people, whether a permission may be exercised against a branch.
    /// </summary>
    /// <param name="role">The system role key, from <c>SystemRoles</c>.</param>
    /// <param name="branch">Whether the resource named is in the caller's branch or the other one.</param>
    /// <param name="permission">The permission the route demands.</param>
    /// <param name="strength">Which of the caller's three sessions to present.</param>
    public async Task<ProbeResult> ProbeAsync(
        string role,
        ProbeBranch branch,
        string permission,
        SessionStrength strength = SessionStrength.Strong)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permission);

        var cookie = _cookies[(role, ProbeBranch.Own, strength)];
        return await SendAsync($"/probe/{Uri.EscapeDataString(permission)}", BranchIdFor(branch), cookie);
    }

    /// <summary>
    /// Asks the same question through a route declared with organisation-wide reach, which is the shape
    /// of a cross-branch read.
    /// </summary>
    /// <remarks>
    /// It exists because the ordinary probes declare the reach their permission implies, and that never
    /// produces the one case <c>docs/prd/workflows/branch-scenarios.md</c> section 3.3 describes: a
    /// branch-scoped permission — finding a customer, reading an order — declared organisation-wide on a
    /// read, so that the Owner and the Auditor can search across branches and nobody else can. The
    /// answer must turn on <c>admin.organisation.read_all_branches</c> and on nothing else.
    /// </remarks>
    /// <param name="role">The system role key.</param>
    /// <param name="branch">Which branch the resource named belongs to.</param>
    /// <param name="permission">The permission the route demands.</param>
    public Task<ProbeResult> ProbeWideReadAsync(string role, ProbeBranch branch, string permission)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permission);

        return SendAsync(
            $"/probe-wide/{Uri.EscapeDataString(permission)}",
            BranchIdFor(branch),
            _cookies[(role, ProbeBranch.Own, SessionStrength.Strong)]);
    }

    /// <summary>
    /// Asks as one of the <b>second cohort</b> — the people seeded in the other branch — with the
    /// resource in the branch named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every test in this collection shares one minute of the fixture's clock, and refusals coalesce per
    /// actor, endpoint and minute. So a test that asserts on <em>what reached the audit trail</em> cannot
    /// use the same people the matrix tests are refusing all over the same endpoints: its own refusal
    /// would be collapsed into one of theirs, and it would be asserting on their entry.
    /// </para>
    /// <para>
    /// The second cohort exists for that. The matrix tests always act as the first cohort, so these
    /// twelve accounts are refused by nobody but the test asking. <see cref="ProbeBranch.Other"/> is
    /// their own branch, and <see cref="ProbeBranch.Own"/> is the one they cannot reach.
    /// </para>
    /// </remarks>
    /// <param name="role">The system role key.</param>
    /// <param name="branch">Which branch the resource named belongs to.</param>
    /// <param name="permission">The permission the route demands.</param>
    /// <param name="strength">Which of that person's three sessions to present.</param>
    /// <param name="write">True to ask as a state-changing request.</param>
    public Task<ProbeResult> ProbeAsSecondCohortAsync(
        string role,
        ProbeBranch branch,
        string permission,
        SessionStrength strength = SessionStrength.Strong,
        bool write = false)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permission);

        var prefix = write ? "probe-write" : "probe";

        return SendAsync(
            $"/{prefix}/{Uri.EscapeDataString(permission)}",
            BranchIdFor(branch),
            _cookies[(role, ProbeBranch.Other, strength)],
            write ? HttpMethod.Post : HttpMethod.Get);
    }

    /// <summary>
    /// Asks as the person assigned to <em>both</em> branches, working in the first, through a route
    /// declared with the reach named.
    /// </summary>
    /// <remarks>
    /// This is the one question the single-branch cohorts cannot ask. For everybody else "the branch my
    /// session is working in" and "a branch I am assigned to" name the same single branch, so an
    /// endpoint declaring <see cref="BranchScope.CurrentBranch"/> and one declaring
    /// <see cref="BranchScope.AssignedBranches"/> answer identically however the handler is written —
    /// including when it ignores the declaration altogether. Somebody with two assignments and one
    /// active branch separates them.
    /// </remarks>
    /// <param name="role">The system role key.</param>
    /// <param name="branch">Which branch the resource named belongs to.</param>
    /// <param name="permission">The permission the route demands.</param>
    /// <param name="reach">
    /// The reach the route declares. <see cref="BranchScope.CurrentBranch"/> uses the ordinary probe;
    /// <see cref="BranchScope.AssignedBranches"/> uses the route declared that way.
    /// </param>
    public Task<ProbeResult> ProbeAsDualBranchAsync(
        string role,
        ProbeBranch branch,
        string permission,
        BranchScope reach)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permission);

        var prefix = reach switch
        {
            BranchScope.CurrentBranch => "probe",
            BranchScope.AssignedBranches => "probe-assigned",
            _ => throw new ArgumentOutOfRangeException(
                nameof(reach),
                reach,
                "The dual-branch cohort exercises the two branch reaches; organisation reach is asked "
                + "through ProbeWideReadAsync, which turns on a permission rather than on assignments."),
        };

        return SendAsync(
            $"/{prefix}/{Uri.EscapeDataString(permission)}",
            BranchIdFor(branch),
            _dualBranchCookies[role]);
    }

    /// <summary>The account behind the person assigned to both branches.</summary>
    /// <param name="role">The system role key.</param>
    public Guid DualBranchUserIdOf(string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return _dualBranchUsers[role];
    }

    /// <summary>The account behind one of the seeded people.</summary>
    /// <param name="role">The system role key.</param>
    /// <param name="branch">Which branch that person works in.</param>
    public Guid UserIdOf(string role, ProbeBranch branch)
    {
        ArgumentNullException.ThrowIfNull(role);
        return _users[(role, branch)];
    }

    /// <summary>The audit entity identifier a refusal at one of the write probes is recorded under.</summary>
    /// <param name="permission">The permission the route demands.</param>
    public static Guid WriteProbeAuditIdentifier(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return AuthorisationDenialAuditingHandler.IdentifierFor(
            $"POST /probe-write/{permission}/{{branchId}}");
    }

    /// <summary>Asks the same question with no session at all.</summary>
    /// <param name="permission">The permission the route demands.</param>
    public Task<ProbeResult> ProbeAnonymouslyAsync(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return SendAsync($"/probe/{Uri.EscapeDataString(permission)}", OwnBranchId, cookie: null);
    }

    /// <summary>Asks against a branch identifier that matches nothing.</summary>
    /// <param name="role">The system role key.</param>
    /// <param name="permission">The permission the route demands.</param>
    public Task<ProbeResult> ProbeUnknownResourceAsync(string role, string permission)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permission);

        return SendAsync(
            $"/probe/{Uri.EscapeDataString(permission)}",
            UnknownBranchId,
            _cookies[(role, ProbeBranch.Own, SessionStrength.Strong)]);
    }

    /// <summary>Opens a context onto this application's database, for assertions about what it wrote.</summary>
    public PlatformDbContext OpenPlatformContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PlatformDbContext(options);
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        DatabaseAvailability.EnsureAvailableInContinuousIntegration();

        if (!IsAvailable)
        {
            return;
        }

        ConnectionString = await CreateDatabaseAsync();
        await SeedAsync();
        await StartHostAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        NpgsqlConnection.ClearAllPools();

        if (_databaseName is not null)
        {
            await OnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)");
        }
    }

    private Guid BranchIdFor(ProbeBranch branch) => branch switch
    {
        ProbeBranch.Own => OwnBranchId,
        ProbeBranch.Other => OtherBranchId,
        _ => UnknownBranchId,
    };

    private async Task<ProbeResult> SendAsync(
        string prefix,
        Guid branchId,
        string? cookie,
        HttpMethod? method = null)
    {
        var client = _client ?? throw new InvalidOperationException("The probe application is not running.");

        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, $"{prefix}/{branchId}");

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{SessionAuthenticationDefaults.CookieName}={cookie}");
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return new ProbeResult(response.StatusCode, await ProblemCodeAsync(response));
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode || response.Content.Headers.ContentLength is null or 0)
        {
            return null;
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken);

            return problem.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> CreateDatabaseAsync()
    {
        // Namespaced per run rather than fixed. A bare constant means two runs against one cluster
        // drop each other's database mid-test — DROP DATABASE ... WITH (FORCE) terminates another
        // process's backends — and the tier then fails inside InitializeAsync for a reason that has
        // nothing to do with authorisation.
        _databaseName = $"{DatabaseAvailability.DatabaseNamespace}_authz";

        await OnMaintenanceDatabaseAsync($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)");
        await OnMaintenanceDatabaseAsync($"CREATE DATABASE {_databaseName}");

        var connectionString = new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = _databaseName,
        }.ConnectionString;

        var platformOptions = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        await using (var platform = new PlatformDbContext(platformOptions))
        {
            await platform.Database.MigrateAsync();
        }

        await using var identity = SessionDatabaseFixture.CreateContext(connectionString);
        await identity.Database.MigrateAsync();

        return connectionString;
    }

    /// <summary>
    /// Writes the roles, the branches, one person per role in each branch, and their sessions.
    /// </summary>
    /// <remarks>
    /// The roles come from the seeder rather than from a fixture of their own, so what the matrix tests
    /// exercise is the grant set a real installation is given by <c>init-reference-data</c> — not a
    /// convenient approximation of it that could be right while the real one is wrong.
    /// </remarks>
    private async Task SeedAsync()
    {
        var clock = new TestClock(Now);

        await using var context = SessionDatabaseFixture.CreateContext(ConnectionString);

        await new IdentityReferenceDataSeeder(context, Catalogue, clock, new UuidV7IdGenerator())
            .SeedSystemRolesAsync(OrganisationId, TestContext.Current.CancellationToken);

        foreach (var branch in (Guid[])[OwnBranchId, OtherBranchId])
        {
            var code = branch == OwnBranchId ? "PROBEA" : "PROBEB";
            context.Branches.Add(Branch.Open(branch, OrganisationId, code, $"Probe branch {code}", Now).Value);
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var roles = await context.Roles
            .Include(role => role.Permissions)
            .Where(role => role.OrganisationId == OrganisationId)
            .ToListAsync(TestContext.Current.CancellationToken);

        foreach (var role in roles)
        {
            _grants[role.Key] = role.PermissionKeys.ToHashSet(StringComparer.Ordinal);
        }

        await using var services = SessionDatabaseFixture.BuildServices(ConnectionString, clock);

        foreach (var role in roles)
        {
            foreach (var branch in (ProbeBranch[])[ProbeBranch.Own, ProbeBranch.Other])
            {
                await SeedPersonAsync(context, services, role, branch);
            }

            await SeedDualBranchPersonAsync(context, services, role);
        }
    }

    private async Task SeedPersonAsync(
        IdentityDbContext context,
        IServiceProvider services,
        Role role,
        ProbeBranch branch)
    {
        var branchId = BranchIdFor(branch);
        var suffix = branch == ProbeBranch.Own ? "a" : "b";

        var user = await SessionTestData.CreateActiveUserAsync(
            context, Now, $"{role.Key}.{suffix}", $"Probe {role.Name} {suffix.ToUpperInvariant()}", branchId);

        context.UserRoles.Add(UserRoleAssignment.Create(user.Id, role.Id, Now));
        context.UserBranchAssignments.Add(
            UserBranchAssignment.Create(user.Id, branchId, Now, isPrimary: true));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _users[(role.Key, branch)] = user.Id;

        _cookies[(role.Key, branch, SessionStrength.Strong)] =
            (await StartSessionAsync(services, user.Id, secondFactor: true)).Token;

        _cookies[(role.Key, branch, SessionStrength.Weak)] =
            (await StartSessionAsync(services, user.Id, secondFactor: false)).Token;

        var stale = await StartSessionAsync(services, user.Id, secondFactor: true);
        _cookies[(role.Key, branch, SessionStrength.Stale)] = stale.Token;

        await AgeAsync(context, stale.SessionId);
    }

    /// <summary>
    /// Seeds one person per role assigned to <em>both</em> branches, working in the first.
    /// </summary>
    /// <remarks>
    /// Everybody else here is assigned to exactly one branch, and while that is true no test can tell
    /// "the branch this session is working in" from "a branch this person is assigned to" — the two
    /// sets are the same set. That is precisely the distinction <see cref="BranchScope.CurrentBranch"/>
    /// and <see cref="BranchScope.AssignedBranches"/> exist to draw, so somebody who can be in one and
    /// not the other has to exist for either value to be worth declaring.
    /// </remarks>
    private async Task SeedDualBranchPersonAsync(
        IdentityDbContext context,
        IServiceProvider services,
        Role role)
    {
        var user = await SessionTestData.CreateActiveUserAsync(
            context, Now, $"{role.Key}.ab", $"Probe {role.Name} AB", OwnBranchId);

        context.UserRoles.Add(UserRoleAssignment.Create(user.Id, role.Id, Now));
        context.UserBranchAssignments.Add(
            UserBranchAssignment.Create(user.Id, OwnBranchId, Now, isPrimary: true));
        context.UserBranchAssignments.Add(
            UserBranchAssignment.Create(user.Id, OtherBranchId, Now, isPrimary: false));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dualBranchUsers[role.Key] = user.Id;
        _dualBranchCookies[role.Key] = (await StartSessionAsync(services, user.Id, secondFactor: true)).Token;
    }

    /// <summary>
    /// Ages one session's last strong authentication past the step-up window.
    /// </summary>
    /// <remarks>
    /// By identifier, not by "the newest row". Every session here is created under one frozen clock, so
    /// <c>created_at</c> is identical across all three of a person's sessions and ordering by it decides
    /// nothing; the identifiers are UUIDv7, ordered by the real wall clock to the millisecond, so three
    /// inserts inside one millisecond order at random. Moving the row rather than the clock is still the
    /// right choice — winding a shared clock forward would leave every session in the application stale
    /// for every test that ran next — but which row has to be a fact, not a guess.
    /// </remarks>
    private static async Task AgeAsync(IdentityDbContext context, Guid sessionId)
        => await context.Database.ExecuteSqlAsync(
            $"""
             UPDATE identity.sessions
             SET last_strong_auth_at = {Now - StaleStepUpAge}
             WHERE id = {sessionId}
             """,
            TestContext.Current.CancellationToken);

    private static async Task<IssuedSession> StartSessionAsync(
        IServiceProvider services,
        Guid userId,
        bool secondFactor)
    {
        using var scope = services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();

        var issued = await sessions.StartAsync(
            new StartSessionRequest(userId, "authorisation probe", MfaSatisfied: secondFactor),
            TestContext.Current.CancellationToken);

        return issued.IsSuccess
            ? issued.Value
            : throw new InvalidOperationException(
                $"Could not start a probe session: {issued.Error.Code}.");
    }

    /// <summary>
    /// Builds the application: the real security registrations, the real session ticket store, and one
    /// route per catalogued permission.
    /// </summary>
    private async Task StartHostAsync()
    {
        var connectionString = ConnectionString;
        var clock = new TestClock(Now);

        _host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
                    services.AddRouting();
                    services.AddProblemDetails();
                    services.AddSingleton<IClock>(clock);
                    services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();

                    services.AddDbContext<IdentityDbContext>(builder => builder
                        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                            ModuleDbContext.MigrationsHistoryTable, IdentityDbContext.SchemaName))
                        .UseSnakeCaseNamingConvention());

                    services.AddDbContext<PlatformDbContext>(builder => builder
                        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                            ModuleDbContext.MigrationsHistoryTable, PlatformDbContext.SchemaName))
                        .UseSnakeCaseNamingConvention());

                    services.AddScoped<IUserAccessQuery, UserAccessQuery>();
                    services.AddScoped<ISessionTicketStore, SessionTicketStore>();
                    services.AddScoped<IAuditContext, ProbeAuditContext>();
                    services.AddScoped<IAuditWriter, AuditWriter>();
                    services.AddSingleton<IResourceScopeResolver>(
                        new ProbeBranchResolver(OwnBranchId, OtherBranchId));

                    // The real registrations, not a subset chosen to make the probes pass: the cookie
                    // scheme, the session-backed caller, every requirement handler, the problem writer
                    // and the denial recorder that wraps it.
                    services.AddTailor360Security();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseTailor360ResourceScope();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        foreach (var permission in Catalogue.All)
                        {
                            MapProbe(endpoints, permission);
                        }
                    });
                }))
            .Build();

        await _host.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
    }

    /// <summary>
    /// Maps one route per permission, of the shape every later route will have.
    /// </summary>
    /// <remarks>
    /// The branch scope is derived from the permission's own scope rather than chosen: a branch-scoped
    /// permission is exercised as a branch action and an organisation-scoped one as an organisation
    /// action, because a probe that declared something else would be measuring a route nobody will
    /// write. <c>ScopedToResource</c> repeats the scope, exactly as the endpoint inventory requires of
    /// a real route, so the resource check honours organisation reach on the reads that declare it.
    /// </remarks>
    private static void MapProbe(IEndpointRouteBuilder endpoints, Permission permission)
    {
        var scope = permission.Scope == PermissionScope.Organisation
            ? BranchScope.Organisation
            : BranchScope.CurrentBranch;

        var route = endpoints
            .MapGet($"/probe/{permission.Key}/{{branchId}}", () => Results.Ok(new { permission.Key }))
            .RequirePermission(permission.Key, scope)
            .ScopedToResource(ProbeResourceKind, "branchId", scope);

        if (permission.RequiresStepUp)
        {
            route.RequireStepUp();
        }

        // The state-changing shape. It is what the denial audit is defined over — a refused attempt to
        // change something — and it is audited, because ARCH-008 requires that of every real route of
        // this shape and a probe that skipped it would be measuring a route nobody may write.
        var write = endpoints
            .MapPost($"/probe-write/{permission.Key}/{{branchId}}", () => Results.Ok(new { permission.Key }))
            .RequirePermission(permission.Key, scope)
            .ScopedToResource(ProbeResourceKind, "branchId", scope)
            .Audited($"probe.{permission.Key}", permission.RequiresReason);

        if (permission.RequiresStepUp)
        {
            write.RequireStepUp();
        }

        // The cross-branch read shape, for branch-scoped permissions only. An organisation-scoped
        // permission has no narrower reading to widen.
        if (permission.Scope == PermissionScope.Organisation)
        {
            return;
        }

        // The shape that spans the caller's assignments rather than their active branch. It exists so
        // that the two branch reaches are both exercised: a handler that read the declaration and one
        // that ignored it would agree on every other route in this application.
        var assigned = endpoints
            .MapGet($"/probe-assigned/{permission.Key}/{{branchId}}", () => Results.Ok(new { permission.Key }))
            .RequirePermission(permission.Key, BranchScope.AssignedBranches)
            .ScopedToResource(ProbeResourceKind, "branchId", BranchScope.AssignedBranches);

        if (permission.RequiresStepUp)
        {
            assigned.RequireStepUp();
        }

        var wide = endpoints
            .MapGet($"/probe-wide/{permission.Key}/{{branchId}}", () => Results.Ok(new { permission.Key }))
            .RequirePermission(permission.Key, BranchScope.Organisation)
            .ScopedToResource(ProbeResourceKind, "branchId", BranchScope.Organisation);

        if (permission.RequiresStepUp)
        {
            wide.RequireStepUp();
        }
    }

    private static async Task OnMaintenanceDatabaseAsync(string sql)
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseAvailability.ConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Answers for the probe resource kind: the resource named <em>is</em> a branch, and its branch is
    /// itself.
    /// </summary>
    /// <remarks>
    /// It answers null for anything that is not one of the two seeded branches, which is what makes the
    /// identifier-editing case testable: a branch that exists and a branch that does not must produce
    /// the same answer, and a resolver that filtered by the caller's branches would have decided that
    /// question here instead of leaving it to the pipeline that writes the audit entry.
    /// </remarks>
    private sealed class ProbeBranchResolver(Guid ownBranchId, Guid otherBranchId) : IResourceScopeResolver
    {
        public string ResourceKind => ProbeResourceKind;

        public ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
            => ValueTask.FromResult(resourceId == ownBranchId || resourceId == otherBranchId
                ? ResourceScope.Unassigned(ProbeResourceKind, resourceId, resourceId)
                : null);
    }

    /// <summary>Who is acting, for the audit entries the denial recorder writes.</summary>
    private sealed class ProbeAuditContext(ICurrentUser currentUser) : IAuditContext
    {
        public Guid? ActorId => currentUser.IsAuthenticated ? currentUser.UserId : null;

        public string ActorDisplayName
            => currentUser.IsAuthenticated ? currentUser.DisplayName : SystemAuditContext.SystemActor;

        public Guid? BranchId => currentUser.Context.BranchId;

        public string? CorrelationId => null;
    }
}

/// <summary>Which branch a probe request names.</summary>
public enum ProbeBranch
{
    /// <summary>The branch the caller is assigned to.</summary>
    Own,

    /// <summary>A branch the caller is not assigned to.</summary>
    Other,

    /// <summary>An identifier matching no branch at all.</summary>
    Unknown,
}

/// <summary>Which of a person's three standing sessions a probe presents.</summary>
public enum SessionStrength
{
    /// <summary>Second factor satisfied, and satisfied recently enough for step-up.</summary>
    Strong,

    /// <summary>Second factor satisfied, but longer ago than the step-up window allows.</summary>
    Stale,

    /// <summary>Signed in, with no second factor satisfied on this session.</summary>
    Weak,
}

/// <summary>What a probe request was answered with.</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Code">The problem document's stable code, or null on success.</param>
public sealed record ProbeResult(HttpStatusCode Status, string? Code)
{
    /// <summary>How a fixture names this outcome.</summary>
    public override string ToString() => Code is null ? ((int)Status).ToString() : $"{(int)Status} {Code}";
}

/// <summary>The collection that shares one probe application.</summary>
[CollectionDefinition(Name)]
public sealed class AuthorisationProbeCollection : ICollectionFixture<AuthorisationProbeApplication>
{
    /// <summary>The collection name.</summary>
    public const string Name = "authorisation-probe";
}
