using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Abstractions.Time;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.UnitTests.Security;

/// <summary>
/// The endpoint's own step-up gate, evaluated on its own.
/// </summary>
/// <remarks>
/// <para>
/// Step-up is enforced twice: once from the permission, whose <c>RequiresStepUp</c> flag every endpoint
/// using it inherits, and once from the endpoint's own <c>.RequireStepUp()</c> declaration. The two are
/// not redundant. Clearing the flag on a permission is a one-word edit that silently weakens every
/// endpoint using it; the endpoint that named the demand out loud keeps demanding it, and ARCH-018 then
/// fails until somebody says in the same pull request that they meant to.
/// </para>
/// <para>
/// So this tier tests the second gate with the first one absent. In the integration tier both fire at
/// once and a refusal proves only that one of them worked.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class StepUpRequirementTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AllowsASessionThatReAuthenticatedInsideTheWindow()
    {
        var context = await EvaluateAsync(new StepUpUser
        {
            IsAuthenticated = true,
            LastReauthenticatedAt = Now.AddMinutes(-4),
        });

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task RefusesASessionWhoseLastStrongFactorIsOlderThanTheWindow()
    {
        var context = await EvaluateAsync(new StepUpUser
        {
            IsAuthenticated = true,
            LastReauthenticatedAt = Now.AddMinutes(-6),
        });

        context.HasSucceeded.ShouldBeFalse();
        Refusal(context).ShouldBe(AuthorisationRefusal.StepUpRequired);
    }

    /// <summary>
    /// A session that never proved a strong factor is refused, rather than being treated as having
    /// nothing to be stale. "No timestamp" is the weakest state, not the freshest.
    /// </summary>
    [Fact]
    public async Task RefusesASessionThatNeverProvedAStrongFactor()
    {
        var context = await EvaluateAsync(new StepUpUser { IsAuthenticated = true });

        context.HasSucceeded.ShouldBeFalse();
        Refusal(context).ShouldBe(AuthorisationRefusal.StepUpRequired);
    }

    [Fact]
    public async Task RefusesACallerWithNoSessionAtAll()
    {
        var context = await EvaluateAsync(new StepUpUser { LastReauthenticatedAt = Now });

        context.HasSucceeded.ShouldBeFalse();
    }

    /// <summary>The window is configuration, so an installation that shortens it is obeyed.</summary>
    [Fact]
    public async Task HonoursAShortenedFreshnessWindow()
    {
        var context = await EvaluateAsync(
            new StepUpUser { IsAuthenticated = true, LastReauthenticatedAt = Now.AddMinutes(-2) },
            freshness: TimeSpan.FromMinutes(1));

        context.HasSucceeded.ShouldBeFalse();
    }

    private static async Task<AuthorizationHandlerContext> EvaluateAsync(
        StepUpUser user,
        TimeSpan? freshness = null)
    {
        var requirement = new StepUpRequirement();
        var context = new AuthorizationHandlerContext([requirement], user: new(), resource: null);

        var handler = new StepUpAuthorisationHandler(
            user,
            new FixedClock(Now),
            Options.Create(new StepUpOptions { Freshness = freshness ?? TimeSpan.FromMinutes(5) }));

        await handler.HandleAsync(context);
        return context;
    }

    private static AuthorisationRefusal? Refusal(AuthorizationHandlerContext context)
        => context.FailureReasons.OfType<RefusalReason>().Select(reason => reason.Refusal).FirstOrDefault();

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;

        public DateOnly TodayIn(TimeZoneInfo branchTimeZone)
            => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, branchTimeZone).DateTime);
    }

    private sealed class StepUpUser : ICurrentUser
    {
        public bool IsAuthenticated { get; init; }

        public Guid UserId => Guid.Empty;

        public string PrincipalId => "probe";

        public string DisplayName => "Probe";

        public OrganisationContext Context { get; } = new(Guid.Empty, null);

        public IReadOnlySet<Guid> AssignedBranches { get; } = new HashSet<Guid>();

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(StringComparer.Ordinal);

        public bool MfaSatisfied => true;

        public bool IsSignInComplete => IsAuthenticated;

        public DateTimeOffset? LastReauthenticatedAt { get; init; }

        public bool HasPermission(string permissionKey) => false;

        public bool CanActInBranch(Guid branchId) => false;
    }
}
