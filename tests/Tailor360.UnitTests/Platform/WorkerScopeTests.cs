using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Background;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// The principal a background job runs as. A job is the one place where authority is chosen rather
/// than presented, so every case here is written from the direction that matters: what a job must
/// <em>not</em> be able to do, and what must stop being possible the moment access is taken away.
/// </summary>
[Trait("Category", "Unit")]
public sealed class WorkerScopeTests
{
    private static readonly Guid BranchA = Guid.Parse("0199b000-0000-7000-8000-00000000000a");
    private static readonly Guid BranchB = Guid.Parse("0199b000-0000-7000-8000-00000000000b");
    private static readonly Guid Organisation = Guid.Parse("0199b000-0000-7000-8000-0000000000c0");
    private static readonly Guid Requester = Guid.Parse("0199b000-0000-7000-8000-0000000000d0");

    /* System jobs ----------------------------------------------------------------------------- */

    [Fact]
    public void ASystemJobRunsAsItsOwnDeclarationAndNothingMore()
    {
        using var provider = Build(out _);
        var factory = provider.GetRequiredService<IWorkerScopeFactory>();

        using var scope = factory.CreateSystemScope(typeof(HousekeepingJob));

        scope.Principal.IsSystem.ShouldBeTrue();
        scope.Principal.PrincipalId.ShouldBe("job:test.housekeeping");
        scope.Principal.Permissions.ShouldBe([PlatformPermissions.DiagnosticsRead]);
        scope.Principal.HasPermission(PlatformPermissions.AuditRead).ShouldBeFalse();
        scope.Principal.CanActInBranch(BranchA).ShouldBeFalse();
    }

    [Fact]
    public void TheJobsPrincipalIsWhatEverythingInsideTheScopeResolves()
    {
        using var provider = Build(out _);
        var factory = provider.GetRequiredService<IWorkerScopeFactory>();

        using var scope = factory.CreateSystemScope(typeof(HousekeepingJob), correlationId: "c-1");

        scope.Services.GetRequiredService<ICurrentUser>().ShouldBeSameAs(scope.Principal);
        scope.Services.GetRequiredService<WorkerPrincipalAccessor>().CorrelationId.ShouldBe("c-1");
        scope.CorrelationId.ShouldBe("c-1");
    }

    [Fact]
    public void AScopeNobodyBoundAPrincipalToPresentsNobody()
    {
        // The outbox dispatcher creates plain scopes of its own, and they must not inherit a job's
        // authority just by being scopes in a host that runs jobs.
        using var provider = Build(out _);
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();

        var caller = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        caller.IsAuthenticated.ShouldBeFalse();
        caller.Permissions.ShouldBeEmpty();
        caller.CanActInBranch(BranchA).ShouldBeFalse();
    }

    [Fact]
    public void AJobThatDeclaresNothingCannotOpenAScope()
    {
        using var provider = Build(out _);
        var factory = provider.GetRequiredService<IWorkerScopeFactory>();

        var thrown = Should.Throw<InvalidOperationException>(
            () => factory.CreateSystemScope(typeof(UndeclaredJob)));

        thrown.Message.ShouldContain("WorkerJob");
    }

    [Fact]
    public void AJobThatNamesAPermissionNoModuleDeclaresCannotOpenAScope()
    {
        using var provider = Build(out _);
        var factory = provider.GetRequiredService<IWorkerScopeFactory>();

        var thrown = Should.Throw<InvalidOperationException>(
            () => factory.CreateSystemScope(typeof(TypoJob)));

        thrown.Message.ShouldContain("orders.does_not_exist");
    }

    [Fact]
    public async Task AJobDeclaringAStepUpPermissionForItsRequesterCannotOpenAScope()
    {
        // Step-up means "re-authenticated in the last few minutes". A queued job never is, so the
        // declaration is refused rather than quietly satisfied.
        using var provider = Build(out _);
        var factory = provider.GetRequiredService<IWorkerScopeFactory>();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => factory.CreateScopeForAsync(
            typeof(StepUpJob),
            new WorkerJobRequest(Requester, SecondFactorSatisfied: true),
            TestContext.Current.CancellationToken));

        thrown.Message.ShouldContain(PlatformPermissions.FeatureFlags);
    }

    [Fact]
    public void ABranchScopedJobWithoutABranchIsRefused()
    {
        using var provider = Build(out _);
        var factory = provider.GetRequiredService<IWorkerScopeFactory>();

        Should.Throw<InvalidOperationException>(() => factory.CreateSystemScope(typeof(OneBranchJob)));
    }

    [Fact]
    public void AJobThatReachesNoBranchIsNotGivenOne()
    {
        using var provider = Build(out _);
        var factory = provider.GetRequiredService<IWorkerScopeFactory>();

        Should.Throw<InvalidOperationException>(
            () => factory.CreateSystemScope(typeof(HousekeepingJob), BranchA));
    }

    [Fact]
    public void AJobDeclaredAsActingForSomeoneCannotRunAsTheSystem()
    {
        using var provider = Build(out _);
        var factory = provider.GetRequiredService<IWorkerScopeFactory>();

        Should.Throw<InvalidOperationException>(
            () => factory.CreateSystemScope(typeof(ExportJob)));
    }

    [Fact]
    public async Task ASystemJobCannotBeGivenARequester()
    {
        using var provider = Build(out _);
        var factory = provider.GetRequiredService<IWorkerScopeFactory>();

        await Should.ThrowAsync<InvalidOperationException>(() => factory.CreateScopeForAsync(
            typeof(HousekeepingJob),
            new WorkerJobRequest(Requester),
            TestContext.Current.CancellationToken));
    }

    /* Jobs acting for a requester ------------------------------------------------------------- */

    [Fact]
    public async Task AJobActingForSomeoneRunsWithTheAccessTheyHoldNow()
    {
        using var provider = Build(out var directory);
        directory.Add(Authority(branches: [BranchA], permissions: [PlatformPermissions.DiagnosticsRead]));

        using var scope = await provider.GetRequiredService<IWorkerScopeFactory>()
            .CreateScopeForAsync(
                typeof(ExportJob),
                new WorkerJobRequest(Requester, BranchA),
                TestContext.Current.CancellationToken);

        scope.Principal.IsSystem.ShouldBeFalse();
        scope.Principal.UserId.ShouldBe(Requester);
        scope.Principal.PrincipalId.ShouldBe(Requester.ToString("n"));
        scope.Principal.Context.OrganisationId.ShouldBe(Organisation);
        scope.Principal.CanActInBranch(BranchA).ShouldBeTrue();
        scope.Principal.CanActInBranch(BranchB).ShouldBeFalse();
        directory.Reads.ShouldBe(
            1, "the requester's access must be read when the job runs, not trusted from the queue.");
    }

    [Fact]
    public async Task AJobIsAbortedWhenTheRequesterHasLostThePermission()
    {
        using var provider = Build(out var directory);
        directory.Add(Authority(branches: [BranchA], permissions: []));

        var thrown = await Should.ThrowAsync<WorkerJobAuthorisationException>(
            () => provider.GetRequiredService<IWorkerScopeFactory>()
                .CreateScopeForAsync(
                    typeof(ExportJob),
                    new WorkerJobRequest(Requester, BranchA),
                    TestContext.Current.CancellationToken));

        thrown.Reason.ShouldBe(WorkerJobRefusal.PermissionNotHeld);
        thrown.Message.ShouldContain(WorkerJobAuthorisationException.Code);
        thrown.RequesterId.ShouldBe(Requester);
    }

    [Fact]
    public async Task AJobIsAbortedWhenTheRequesterWasSuspended()
    {
        // The queue is not a grant: work queued yesterday must not run on yesterday's authority.
        using var provider = Build(out var directory);
        directory.Add(Authority(
            branches: [BranchA],
            permissions: [PlatformPermissions.DiagnosticsRead],
            isActive: false));

        var thrown = await Should.ThrowAsync<WorkerJobAuthorisationException>(
            () => provider.GetRequiredService<IWorkerScopeFactory>()
                .CreateScopeForAsync(
                    typeof(ExportJob),
                    new WorkerJobRequest(Requester, BranchA),
                    TestContext.Current.CancellationToken));

        thrown.Reason.ShouldBe(WorkerJobRefusal.RequesterNotActive);
    }

    [Fact]
    public async Task AJobIsAbortedWhenTheRequesterNoLongerExists()
    {
        using var provider = Build(out _);

        var thrown = await Should.ThrowAsync<WorkerJobAuthorisationException>(
            () => provider.GetRequiredService<IWorkerScopeFactory>()
                .CreateScopeForAsync(
                    typeof(ExportJob),
                    new WorkerJobRequest(Requester, BranchA),
                    TestContext.Current.CancellationToken));

        thrown.Reason.ShouldBe(WorkerJobRefusal.UnknownRequester);
    }

    [Fact]
    public async Task AJobIsAbortedWhenTheRequesterLeftTheBranch()
    {
        using var provider = Build(out var directory);
        directory.Add(Authority(branches: [BranchB], permissions: [PlatformPermissions.DiagnosticsRead]));

        var thrown = await Should.ThrowAsync<WorkerJobAuthorisationException>(
            () => provider.GetRequiredService<IWorkerScopeFactory>()
                .CreateScopeForAsync(
                    typeof(ExportJob),
                    new WorkerJobRequest(Requester, BranchA),
                    TestContext.Current.CancellationToken));

        thrown.Reason.ShouldBe(WorkerJobRefusal.BranchNotAssigned);
    }

    [Fact]
    public async Task AJobAcrossAssignedBranchesIsAbortedWhenNoBranchIsLeft()
    {
        using var provider = Build(out var directory);
        directory.Add(Authority(branches: [], permissions: [PlatformPermissions.DiagnosticsRead]));

        var thrown = await Should.ThrowAsync<WorkerJobAuthorisationException>(
            () => provider.GetRequiredService<IWorkerScopeFactory>()
                .CreateScopeForAsync(
                    typeof(WorkQueueJob),
                    new WorkerJobRequest(Requester),
                    TestContext.Current.CancellationToken));

        thrown.Reason.ShouldBe(WorkerJobRefusal.NoBranchAssigned);
    }

    [Fact]
    public async Task AJobReachingEveryBranchIsAbortedWhenThatReachWasRemoved()
    {
        using var provider = Build(out var directory);
        directory.Add(Authority(branches: [BranchA], permissions: [PlatformPermissions.DiagnosticsRead]));

        var thrown = await Should.ThrowAsync<WorkerJobAuthorisationException>(
            () => provider.GetRequiredService<IWorkerScopeFactory>().CreateScopeForAsync(
                typeof(CrossBranchReportJob),
                new WorkerJobRequest(Requester, SecondFactorSatisfied: true),
                TestContext.Current.CancellationToken));

        thrown.Reason.ShouldBe(WorkerJobRefusal.OrganisationReachNotHeld);
    }

    [Fact]
    public async Task AJobReachingEveryBranchCarriesThatReachAsAPermissionToo()
    {
        using var provider = Build(out var directory);
        directory.Add(Authority(
            branches: [BranchA],
            permissions: [PlatformPermissions.DiagnosticsRead, PlatformPermissions.ReadAllBranches]));

        using var scope = await provider.GetRequiredService<IWorkerScopeFactory>().CreateScopeForAsync(
            typeof(CrossBranchReportJob),
            new WorkerJobRequest(Requester, SecondFactorSatisfied: true),
            TestContext.Current.CancellationToken);

        scope.Principal.HasPermission(PlatformPermissions.ReadAllBranches).ShouldBeTrue();
        scope.Principal.CanActInBranch(BranchB).ShouldBeTrue();
    }

    [Fact]
    public async Task AJobIsAbortedWhenTheSessionThatQueuedItHadNoSecondFactor()
    {
        using var provider = Build(out var directory);
        directory.Add(Authority(
            branches: [BranchA],
            permissions: [PlatformPermissions.DiagnosticsRead, PlatformPermissions.ReadAllBranches]));

        var thrown = await Should.ThrowAsync<WorkerJobAuthorisationException>(
            () => provider.GetRequiredService<IWorkerScopeFactory>().CreateScopeForAsync(
                typeof(CrossBranchReportJob),
                new WorkerJobRequest(Requester),
                TestContext.Current.CancellationToken));

        thrown.Reason.ShouldBe(WorkerJobRefusal.SecondFactorNotSatisfied);
    }

    [Fact]
    public async Task AJobNeverHoldsMoreThanItDeclaredHoweverPowerfulItsRequester()
    {
        using var provider = Build(out var directory);
        directory.Add(Authority(
            branches: [BranchA],
            permissions:
            [
                PlatformPermissions.DiagnosticsRead,
                PlatformPermissions.AuditRead,
                PlatformPermissions.OutboxReplay,
            ]));

        using var scope = await provider.GetRequiredService<IWorkerScopeFactory>()
            .CreateScopeForAsync(
                typeof(ExportJob),
                new WorkerJobRequest(Requester, BranchA),
                TestContext.Current.CancellationToken);

        scope.Principal.Permissions.ShouldBe([PlatformPermissions.DiagnosticsRead]);
        scope.Principal.HasPermission(PlatformPermissions.OutboxReplay).ShouldBeFalse();
    }

    [Fact]
    public async Task NoJobIsEverFreshEnoughForAStepUp()
    {
        using var provider = Build(out var directory);
        directory.Add(Authority(branches: [BranchA], permissions: [PlatformPermissions.DiagnosticsRead]));

        using var scope = await provider.GetRequiredService<IWorkerScopeFactory>()
            .CreateScopeForAsync(
                typeof(ExportJob),
                new WorkerJobRequest(Requester, BranchA),
                TestContext.Current.CancellationToken);

        scope.Principal.LastReauthenticatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task WithNoAccountsModuleComposedEveryJobActingForSomeoneIsRefused()
    {
        // The fail-closed default: a host that cannot answer "may this person still do this?" answers no.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTailor360WorkerScopes();
        await using var provider = services.BuildServiceProvider();

        var thrown = await Should.ThrowAsync<WorkerJobAuthorisationException>(
            () => provider.GetRequiredService<IWorkerScopeFactory>()
                .CreateScopeForAsync(
                    typeof(ExportJob),
                    new WorkerJobRequest(Requester, BranchA),
                    TestContext.Current.CancellationToken));

        thrown.Reason.ShouldBe(WorkerJobRefusal.UnknownRequester);
    }

    /* Fixtures -------------------------------------------------------------------------------- */

    private static RequesterAuthority Authority(
        IEnumerable<Guid> branches,
        IEnumerable<string> permissions,
        bool isActive = true)
        => new(
            Requester,
            "Synthetic Requester",
            Organisation,
            isActive,
            branches.ToHashSet(),
            permissions.ToHashSet(StringComparer.Ordinal));

    private static ServiceProvider Build(out RecordingAuthorityStore directory)
    {
        directory = new RecordingAuthorityStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTailor360WorkerScopes();
        services.AddSingleton<IRequesterAuthorityStore>(directory);

        return services.BuildServiceProvider();
    }

    private sealed class RecordingAuthorityStore : IRequesterAuthorityStore
    {
        private readonly Dictionary<Guid, RequesterAuthority> _accounts = [];

        public int Reads { get; private set; }

        public void Add(RequesterAuthority authority) => _accounts[authority.UserId] = authority;

        public Task<RequesterAuthority?> FindAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(_accounts.GetValueOrDefault(userId));
        }
    }

    [WorkerJob("test.housekeeping", WorkerBranchScope.None, PlatformPermissions.DiagnosticsRead)]
    private sealed class HousekeepingJob;

    [WorkerJob("test.one_branch", WorkerBranchScope.OneBranch)]
    private sealed class OneBranchJob;

    [WorkerJob("test.typo", WorkerBranchScope.None, "orders.does_not_exist")]
    private sealed class TypoJob;

    [WorkerJob(
        "test.export",
        WorkerBranchScope.OneBranch,
        PlatformPermissions.DiagnosticsRead,
        ActsForRequester = true)]
    private sealed class ExportJob;

    [WorkerJob("test.work_queue", WorkerBranchScope.AssignedBranches, ActsForRequester = true)]
    private sealed class WorkQueueJob;

    [WorkerJob(
        "test.cross_branch_report",
        WorkerBranchScope.Organisation,
        PlatformPermissions.DiagnosticsRead,
        ActsForRequester = true)]
    private sealed class CrossBranchReportJob;

    [WorkerJob(
        "test.step_up",
        WorkerBranchScope.None,
        PlatformPermissions.FeatureFlags,
        ActsForRequester = true)]
    private sealed class StepUpJob;

    private sealed class UndeclaredJob;
}
