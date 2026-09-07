using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Endpoints;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Security;

/// <summary>
/// Resource-scoped authorisation through a real request pipeline: routing, the resolution step,
/// the handlers and the problem document a client actually receives.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is assembled here rather than taken from the web host because no module publishes a
/// permissioned route yet. What is under test is the arrangement itself — that the resource is loaded
/// between routing and authorisation, that the answer is decided against it, and that a host which omits
/// the step refuses rather than waves requests through.
/// </para>
/// <para>
/// No database: the resolver is the seam a module fills, and the point of the seam is that the pipeline
/// does not know or care where the row came from.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ResourceScopePipelineTests
{
    private static readonly Guid BranchA = Guid.Parse("0199a100-0000-7000-8000-00000000000a");
    private static readonly Guid BranchB = Guid.Parse("0199a100-0000-7000-8000-00000000000b");
    private static readonly Guid TheirJob = Guid.Parse("0199a100-0000-7000-8000-000000000001");
    private static readonly Guid SomebodyElsesJob = Guid.Parse("0199a100-0000-7000-8000-000000000002");
    private static readonly Guid AnotherBranchesJob = Guid.Parse("0199a100-0000-7000-8000-000000000003");
    private static readonly Guid NoSuchJob = Guid.Parse("0199a100-0000-7000-8000-0000000000ff");
    private static readonly Guid Tailor = Guid.Parse("0199a100-0000-7000-8000-0000000000f1");
    private static readonly Guid AnotherTailor = Guid.Parse("0199a100-0000-7000-8000-0000000000f2");

    [Fact]
    public async Task ServesTheJobAssignedToTheCallerInTheirOwnBranch()
    {
        using var host = await StartAsync(Caller());

        var response = await host.GetTestClient().GetAsync(Job(TheirJob), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnswersAJobInAnotherBranchExactlyAsItAnswersAJobThatDoesNotExist()
    {
        // The IDOR case. Editing the identifier in the address bar has to be uninformative, and the only
        // way to be sure is to compare the two answers rather than to reason about them.
        using var host = await StartAsync(Caller());
        var client = host.GetTestClient();

        var foreign = await client.GetAsync(Job(AnotherBranchesJob), TestContext.Current.CancellationToken);
        var missing = await client.GetAsync(Job(NoSuchJob), TestContext.Current.CancellationToken);

        var foreignCode = await CodeOf(foreign);
        var missingCode = await CodeOf(missing);

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(foreign.StatusCode);
        missingCode.ShouldBe(foreignCode);
        foreignCode.ShouldBe(AuthorisationProblemResultHandler.ResourceNotFoundCode);
    }

    [Fact]
    public async Task AnswersAMalformedIdentifierTheSameWayAgain()
    {
        using var host = await StartAsync(Caller());

        var response = await host.GetTestClient().GetAsync(
            new Uri("/jobs/not-a-guid", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RefusesAJobInTheCallersBranchThatIsAssignedToSomebodyElse()
    {
        using var host = await StartAsync(Caller());

        var response = await host.GetTestClient().GetAsync(
            Job(SomebodyElsesJob), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(response)).ShouldBe(AuthorisationProblemResultHandler.NotAssignedCode);
    }

    [Fact]
    public async Task LetsTheWorkshopLeadOpenAnybodysJobInTheirBranch()
    {
        var lead = Caller();
        lead.Permissions.Add(OrdersPermissions.Assign);

        using var host = await StartAsync(lead);

        var response = await host.GetTestClient().GetAsync(
            Job(SomebodyElsesJob), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RefusesTheWorkshopLeadAJobInAnotherBranchAllTheSame()
    {
        // Supervising the assignment is not reach. The branch check runs on its own and answers first.
        var lead = Caller();
        lead.Permissions.Add(OrdersPermissions.Assign);

        using var host = await StartAsync(lead);

        var response = await host.GetTestClient().GetAsync(
            Job(AnotherBranchesJob), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TellsACallerWithoutThePermissionNothingAboutTheResource()
    {
        var stranger = Caller();
        stranger.Permissions.Remove(OrdersPermissions.Read);

        using var host = await StartAsync(stranger);

        var response = await host.GetTestClient().GetAsync(
            Job(AnotherBranchesJob), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(response)).ShouldBe(AuthorisationProblemResultHandler.ForbiddenCode);
    }

    [Fact]
    public async Task RefusesEverybodyWhenTheHostForgotTheResolutionStep()
    {
        // The failure mode that would otherwise be silent: without the step, nothing loads the row, and
        // a handler that treated "no resource" as "no resource to check" would serve every job to
        // everybody. This asserts the opposite — the caller who should pass is refused too.
        using var host = await StartAsync(Caller(), resolveResources: false);

        var response = await host.GetTestClient().GetAsync(Job(TheirJob), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeOf(response)).ShouldBe(AuthorisationProblemResultHandler.ForbiddenCode);
    }

    [Fact]
    public async Task LoadsTheResourceOnceAndBeforeTheEndpointRuns()
    {
        var resolver = new RecordingResolver();
        using var host = await StartAsync(Caller(), resolver: resolver);

        await host.GetTestClient().GetAsync(Job(TheirJob), TestContext.Current.CancellationToken);

        resolver.Calls.ShouldBe(1);
        resolver.ResolvedBeforeTheEndpoint.ShouldBeTrue();
    }

    private static Uri Job(Guid id) => new($"/jobs/{id}", UriKind.Relative);

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        return problem.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static TestCaller Caller() => new()
    {
        UserId = Tailor,
        Branch = BranchA,
        Permissions = { OrdersPermissions.Read },
    };

    private static async Task<IHost> StartAsync(
        TestCaller caller,
        bool resolveResources = true,
        RecordingResolver? resolver = null)
    {
        var jobs = new Dictionary<Guid, ResourceScope>
        {
            [TheirJob] = new("orders.garment_job", TheirJob, BranchA, new HashSet<Guid> { Tailor }),
            [SomebodyElsesJob] = new(
                "orders.garment_job", SomebodyElsesJob, BranchA, new HashSet<Guid> { AnotherTailor }),
            [AnotherBranchesJob] = new(
                "orders.garment_job", AnotherBranchesJob, BranchB, new HashSet<Guid> { Tailor }),
        };

        resolver ??= new RecordingResolver();
        resolver.Jobs = jobs;

        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddProblemDetails();
                    services.AddAuthorization();
                    services.AddTailor360PermissionCatalogue();
                    services.AddScoped<ICurrentUser>(_ => caller);
                    services.AddScoped<ResourceScopeContext>();
                    services.AddScoped<IAuthorizationHandler, PermissionAuthorisationHandler>();
                    services.AddScoped<IAuthorizationHandler, BranchScopeAuthorisationHandler>();
                    services.AddScoped<IAuthorizationHandler, ResourceBranchAuthorisationHandler>();
                    services.AddScoped<IAuthorizationHandler, ResourceOwnershipAuthorisationHandler>();
                    services.AddSingleton<
                        IAuthorizationMiddlewareResultHandler, AuthorisationProblemResultHandler>();
                    services.AddSingleton<IResourceScopeResolver>(resolver);
                    services.AddOptions<StepUpOptions>();
                    services.AddSingleton<IClock, SystemClock>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.Use(async (context, next) =>
                    {
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));
                        await next(context);
                    });

                    if (resolveResources)
                    {
                        app.UseTailor360ResourceScope();
                    }

                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet("/jobs/{jobId}", (HttpContext context) =>
                        {
                            resolver.SawTheEndpoint = true;
                            return Results.Ok(new { ok = true });
                        })
                        .RequirePermission(OrdersPermissions.Read)
                        .ScopedToResource("orders.garment_job", "jobId")
                        .RequireAssignment(OrdersPermissions.Assign));
                }))
            .StartAsync(TestContext.Current.CancellationToken);

        return host;
    }

    /// <summary>A module's resolver, in miniature, that also records when it ran.</summary>
    private sealed class RecordingResolver : IResourceScopeResolver
    {
        public IReadOnlyDictionary<Guid, ResourceScope> Jobs { get; set; } =
            new Dictionary<Guid, ResourceScope>();

        public int Calls { get; private set; }

        public bool SawTheEndpoint { get; set; }

        public bool ResolvedBeforeTheEndpoint { get; private set; }

        public string ResourceKind => "orders.garment_job";

        public ValueTask<ResourceScope?> ResolveAsync(Guid resourceId, CancellationToken cancellationToken)
        {
            Calls++;
            ResolvedBeforeTheEndpoint = !SawTheEndpoint;
            return ValueTask.FromResult(Jobs.GetValueOrDefault(resourceId));
        }
    }

    /// <summary>The caller under test. Mutable, because each scenario is one caller and one host.</summary>
    private sealed class TestCaller : ICurrentUser
    {
        public required Guid UserId { get; init; }

        public required Guid Branch { get; init; }

        public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);

        public bool IsAuthenticated => true;

        public string PrincipalId => UserId.ToString("n");

        public string DisplayName => "Test Caller";

        public OrganisationContext Context => new(Guid.Empty, Branch);

        IReadOnlySet<Guid> ICurrentUser.AssignedBranches => new HashSet<Guid> { Branch };

        IReadOnlySet<string> ICurrentUser.Permissions => Permissions;

        public bool MfaSatisfied => true;

        public bool IsSignInComplete => true;

        public DateTimeOffset? LastReauthenticatedAt => null;

        public bool HasPermission(string permissionKey) => Permissions.Contains(permissionKey);

        public bool CanActInBranch(Guid branchId) => branchId == Branch;
    }
}
