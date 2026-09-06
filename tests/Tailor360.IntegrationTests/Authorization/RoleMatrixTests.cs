using Shouldly;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Platform.Abstractions.Multitenancy;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Authorization;

/// <summary>
/// The authorisation matrix, driven: every permission the catalogue declares, asked for by every role
/// the installation seeds, in the branch they work in and in one they do not.
/// </summary>
/// <remarks>
/// <para>
/// The expected answers are not written down here. Which roles hold which permission is read from the
/// seeded role register — the rows <c>init-reference-data</c> writes, which the unit tier holds equal to
/// the owner-approved matrix — and what each class of caller is owed is read from
/// <c>matrix.yaml</c>. So a grant moved in the matrix document moves an HTTP outcome here, and a grant
/// moved only in the code fails the unit tier. There is nowhere to change one without the other
/// noticing, which is the property the whole design is for.
/// </para>
/// <para>
/// <b>What is being asked.</b> Not whether a handler computes the right answer — the unit tier asks
/// that, in isolation, and much faster. This asks the owner's question: if a Tailor in Coimbatore opens
/// this, what happens? The answer comes back through a real route table, a real cookie, a real session
/// row rebuilt from a real database, and the real refusal document a client would parse.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(AuthorisationProbeCollection.Name)]
public sealed class RoleMatrixTests(AuthorisationProbeApplication probe)
{
    private static readonly MatrixFixtures Fixtures = MatrixFixtures.Load();

    /// <summary>
    /// The matrix itself: for every permission and every role, the approved grant is exactly what the
    /// endpoint answers in the caller's own branch.
    /// </summary>
    [Fact]
    public async Task EveryRoleIsAnsweredInItsOwnBranchExactlyAsTheMatrixApproves()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var allowed = Fixtures.Expected("holder-own-branch");
        var refused = Fixtures.Expected("non-holder-own-branch");
        var disagreements = new List<string>();
        var checks = 0;

        foreach (var permission in probe.Catalogue.All)
        {
            foreach (var role in SystemRoles.All)
            {
                var holds = probe.GrantsOf(role.Key).Contains(permission.Key);
                var expected = Gap(role.Key, permission.Key)?.OwnBranch
                               ?? (holds ? allowed : refused);

                var actual = await probe.ProbeAsync(role.Key, ProbeBranch.Own, permission.Key);
                checks++;

                if (actual != expected.AsResult())
                {
                    disagreements.Add(
                        $"{role.Key} {(holds ? "holds" : "does not hold")} '{permission.Key}' and was "
                        + $"answered {actual} in its own branch; the matrix expects {expected.AsResult()}.");
                }
            }
        }

        checks.ShouldBe(probe.Catalogue.All.Count * SystemRoles.All.Count);
        checks.ShouldBeGreaterThan(0, "Nothing was exercised, so nothing was proved.");

        disagreements.ShouldBeEmpty(
            "The approved grants and the answers the application gives disagree:\n  "
            + string.Join("\n  ", disagreements));
    }

    /// <summary>
    /// A branch-scoped permission does not travel. Holding it in Coimbatore is not holding it in
    /// Chennai, and the refusal says nothing about whether the record exists.
    /// </summary>
    [Fact]
    public async Task NoBranchScopedPermissionReachesAnotherBranch()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var expected = Fixtures.Expected("holder-other-branch").AsResult();
        var reached = new List<string>();
        var checks = 0;

        foreach (var permission in probe.Catalogue.All.Where(p => p.Scope == PermissionScope.Branch))
        {
            foreach (var role in SystemRoles.All.Where(r => probe.GrantsOf(r.Key).Contains(permission.Key)))
            {
                var actual = await probe.ProbeAsync(role.Key, ProbeBranch.Other, permission.Key);
                checks++;

                if (actual != expected)
                {
                    reached.Add(
                        $"{role.Key} holds '{permission.Key}' and was answered {actual} against another "
                        + $"branch; it must be answered {expected}.");
                }
            }
        }

        checks.ShouldBeGreaterThan(0, "No branch-scoped grant was exercised across branches.");
        reached.ShouldBeEmpty(
            "These grants reached outside the branches they were granted in:\n  "
            + string.Join("\n  ", reached));
    }

    /// <summary>
    /// An organisation-scoped permission is about the organisation, so the branch a request names does
    /// not narrow it. What still narrows it is who holds it, and the reach that makes it usable.
    /// </summary>
    [Fact]
    public async Task AnOrganisationScopedPermissionIsNotNarrowedByTheBranchNamed()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var expected = Fixtures.Expected("reach-other-branch").AsResult();
        var wrong = new List<string>();
        var checks = 0;

        foreach (var permission in probe.Catalogue.All.Where(p => p.Scope == PermissionScope.Organisation))
        {
            foreach (var role in SystemRoles.All.Where(r => probe.GrantsOf(r.Key).Contains(permission.Key)))
            {
                var gap = Gap(role.Key, permission.Key);
                var answer = gap?.OtherBranch.AsResult() ?? expected;
                var actual = await probe.ProbeAsync(role.Key, ProbeBranch.Other, permission.Key);

                checks++;

                if (actual != answer)
                {
                    wrong.Add(
                        $"{role.Key} holds '{permission.Key}' and naming another branch was answered "
                        + $"{actual}, not {answer}.");
                }
            }
        }

        checks.ShouldBeGreaterThan(0, "No organisation-scoped grant was exercised.");
        wrong.ShouldBeEmpty(string.Join("\n  ", wrong));
    }

    /// <summary>
    /// The cross-branch read, and the exact thing that decides it. Organisation reach is a permission,
    /// not a consequence of seniority and not a consequence of holding several branches.
    /// </summary>
    [Fact]
    public async Task OnlyOrganisationReachReadsAcrossBranches()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var withReach = Fixtures.Expected("wide-read-with-reach").AsResult();
        var withoutReach = Fixtures.Expected("wide-read-without-reach").AsResult();
        var wrong = new List<string>();
        var reachChecks = 0;

        foreach (var permission in probe.Catalogue.All.Where(p => p.Scope == PermissionScope.Branch))
        {
            foreach (var role in SystemRoles.All.Where(r => probe.GrantsOf(r.Key).Contains(permission.Key)))
            {
                var reaches = probe.GrantsOf(role.Key)
                    .Contains(BranchScopeAuthorisationHandler.OrganisationWidePermission);

                var expected = reaches ? withReach : withoutReach;
                var actual = await probe.ProbeWideReadAsync(role.Key, ProbeBranch.Other, permission.Key);

                if (reaches)
                {
                    reachChecks++;
                }

                if (actual != expected)
                {
                    wrong.Add(
                        $"{role.Key} {(reaches ? "holds" : "does not hold")} organisation reach and reading "
                        + $"'{permission.Key}' in another branch was answered {actual}, not {expected}.");
                }
            }
        }

        reachChecks.ShouldBeGreaterThan(
            0, "No role with organisation reach was exercised, so the permission proved nothing.");

        wrong.ShouldBeEmpty("The cross-branch read did not turn on organisation reach:\n  "
                            + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// A second assigned branch is reachable only where the endpoint declares that reach. The person
    /// here is assigned to both branches and working in the first; the row they name is in the second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the test no other caller in the suite can stand in for. Everybody else is assigned to
    /// exactly one branch, so "the branch this session is working in" and "a branch this person is
    /// assigned to" name the same branch and a handler that read the declared scope and one that
    /// ignored it would agree on every request. A caller for whom the two sets differ is the only thing
    /// that separates them, and the separation is the whole content of the two enum values.
    /// </para>
    /// <para>
    /// The refusal is the important half. <c>current-branch</c> is the default every endpoint gets, and
    /// if it silently meant "any branch you are assigned to" then a matrix row reading
    /// <c>current-branch</c> would be approving something wider than it says — the one failure a
    /// generated matrix cannot catch, because the row and the declaration would still agree.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASecondAssignedBranchIsReachedOnlyWhereTheEndpointDeclaresThatReach()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var refused = Fixtures.Expected("second-branch-not-current").AsResult();
        var allowed = Fixtures.Expected("second-branch-assigned-reach").AsResult();
        var wrong = new List<string>();
        var checks = 0;

        foreach (var permission in probe.Catalogue.All.Where(p => p.Scope == PermissionScope.Branch))
        {
            foreach (var role in SystemRoles.All.Where(r => probe.GrantsOf(r.Key).Contains(permission.Key)))
            {
                var current = await probe.ProbeAsDualBranchAsync(
                    role.Key, ProbeBranch.Other, permission.Key, BranchScope.CurrentBranch);

                var assigned = await probe.ProbeAsDualBranchAsync(
                    role.Key, ProbeBranch.Other, permission.Key, BranchScope.AssignedBranches);

                checks++;

                if (current != refused || assigned != allowed)
                {
                    wrong.Add(
                        $"{role.Key}, assigned to both branches and working in the first, exercising "
                        + $"'{permission.Key}' against the second was answered {current} on a "
                        + $"current-branch endpoint and {assigned} on an assigned-branches one; "
                        + $"expected {refused} and {allowed}.");
                }
            }
        }

        checks.ShouldBeGreaterThan(
            0, "No two-branch caller was exercised, so the two branch reaches were never separated.");

        wrong.ShouldBeEmpty(
            "The declared branch reach did not decide the answer:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// And in their own branch the same person is allowed under either declaration, so the refusal
    /// above is about the branch named and not about being assigned to two of them.
    /// </summary>
    [Fact]
    public async Task ATwoBranchCallerIsAnsweredNormallyInTheBranchTheyAreWorkingIn()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var allowed = Fixtures.Expected("holder-own-branch").AsResult();
        var wrong = new List<string>();
        var checks = 0;

        foreach (var permission in probe.Catalogue.All.Where(p => p.Scope == PermissionScope.Branch))
        {
            foreach (var role in SystemRoles.All.Where(r => probe.GrantsOf(r.Key).Contains(permission.Key)))
            {
                var current = await probe.ProbeAsDualBranchAsync(
                    role.Key, ProbeBranch.Own, permission.Key, BranchScope.CurrentBranch);

                var assigned = await probe.ProbeAsDualBranchAsync(
                    role.Key, ProbeBranch.Own, permission.Key, BranchScope.AssignedBranches);

                checks++;

                if (current != allowed || assigned != allowed)
                {
                    wrong.Add(
                        $"{role.Key} exercising '{permission.Key}' in the branch they are working in was "
                        + $"answered {current} and {assigned}; both must be {allowed}.");
                }
            }
        }

        checks.ShouldBeGreaterThan(0, "No two-branch caller was exercised in their own branch.");
        wrong.ShouldBeEmpty(string.Join("\n  ", wrong));
    }

    /// <summary>
    /// The recorded disagreements between approval and enforcement are exactly the ones that exist.
    /// </summary>
    /// <remarks>
    /// It fails in both directions on purpose. A new disagreement fails because nobody wrote it down;
    /// a fixed one fails because the entry outlived the problem, and an exception list nobody prunes
    /// becomes a list nobody reads.
    /// </remarks>
    [Fact]
    public async Task TheRecordedReachGapsAreExactlyTheGapsThatExist()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var allowed = Fixtures.Expected("holder-own-branch").AsResult();
        var recorded = Fixtures.OrganisationReachGaps
            .Select(gap => $"{gap.Role} / {gap.Permission}")
            .ToHashSet(StringComparer.Ordinal);

        recorded.ShouldNotBeEmpty(
            "There are no recorded gaps. If that is now true, delete this test with the list; if it is "
            + "not, the fixtures have lost them.");

        var actual = new List<string>();

        foreach (var permission in probe.Catalogue.All.Where(p => p.Scope == PermissionScope.Organisation))
        {
            foreach (var role in SystemRoles.All.Where(r => probe.GrantsOf(r.Key).Contains(permission.Key)))
            {
                var answered = await probe.ProbeAsync(role.Key, ProbeBranch.Own, permission.Key);
                if (answered != allowed)
                {
                    actual.Add($"{role.Key} / {permission.Key}");
                }
            }
        }

        actual.Order(StringComparer.Ordinal).ShouldBe(
            recorded.Order(StringComparer.Ordinal),
            "The organisation-scoped permissions a role is approved to hold but cannot exercise have "
            + "changed. Every one of them is a place where the owner approved something the software "
            + $"refuses; record it in {MatrixFixtures.RelativePath} with a reason and the issue that "
            + "settles it, or fix it.");
    }

    /// <summary>Nothing in the catalogue is reachable without a session.</summary>
    [Fact]
    public async Task NoPermissionIsReachableWithoutASession()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var expected = Fixtures.Expected("no-session").AsResult();
        var reachable = new List<string>();

        foreach (var permission in probe.Catalogue.All)
        {
            var actual = await probe.ProbeAnonymouslyAsync(permission.Key);
            if (actual != expected)
            {
                reachable.Add($"'{permission.Key}' answered {actual} with no session, not {expected}.");
            }
        }

        reachable.ShouldBeEmpty("Deny by default did not hold:\n  " + string.Join("\n  ", reachable));
    }

    /// <summary>
    /// An identifier that matches nothing is answered exactly as one belonging to another branch. This
    /// is the whole of the identifier-editing defence: the two cases must be indistinguishable, and the
    /// only way to be sure is to compare the answers rather than to reason about them.
    /// </summary>
    [Fact]
    public async Task AnIdentifierThatMatchesNothingIsAnsweredAsAnotherBranchsIs()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var expected = Fixtures.Expected("unknown-resource").AsResult();
        var told = new List<string>();

        foreach (var permission in probe.Catalogue.All.Where(p => p.Scope == PermissionScope.Branch))
        {
            var holder = SystemRoles.All.FirstOrDefault(
                role => probe.GrantsOf(role.Key).Contains(permission.Key));

            if (holder is null)
            {
                continue;
            }

            var unknown = await probe.ProbeUnknownResourceAsync(holder.Key, permission.Key);
            var otherBranch = await probe.ProbeAsync(holder.Key, ProbeBranch.Other, permission.Key);

            if (unknown != otherBranch || unknown != expected)
            {
                told.Add(
                    $"{holder.Key} asking for '{permission.Key}' was told {unknown} about an identifier "
                    + $"matching nothing and {otherBranch} about another branch's. Both must be {expected}.");
            }
        }

        told.ShouldBeEmpty(
            "Editing an identifier revealed whether the record exists:\n  " + string.Join("\n  ", told));
    }

    /// <summary>
    /// A permission flagged for multi-factor authentication is refused to a session that has not
    /// satisfied one, whatever that session's roles grant.
    /// </summary>
    [Fact]
    public async Task AFlaggedPermissionIsRefusedToASessionWithNoSecondFactor()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var expected = Fixtures.Expected("flagged-without-second-factor").AsResult();
        var served = new List<string>();
        var checks = 0;

        foreach (var permission in probe.Catalogue.All.Where(p => p.RequiresMfa))
        {
            foreach (var role in SystemRoles.All.Where(r => probe.GrantsOf(r.Key).Contains(permission.Key)))
            {
                var actual = await probe.ProbeAsync(
                    role.Key, ProbeBranch.Own, permission.Key, SessionStrength.Weak);

                checks++;

                if (actual != expected)
                {
                    served.Add(
                        $"{role.Key} was answered {actual} for '{permission.Key}' on a session with no "
                        + $"second factor; it must be {expected}.");
                }
            }
        }

        checks.ShouldBeGreaterThan(0, "No flagged permission was exercised, so nothing was proved.");
        served.ShouldBeEmpty(string.Join("\n  ", served));
    }

    /// <summary>
    /// The fresh and stale halves of step-up, on the same person and the same permission. A session
    /// that answered a factor half an hour ago is refused; one that answered it just now is not.
    /// </summary>
    [Fact]
    public async Task AStepUpPermissionSeparatesAFreshSessionFromAStaleOne()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        var fresh = Fixtures.Expected("flagged-fresh-step-up").AsResult();
        var stale = Fixtures.Expected("flagged-stale-step-up").AsResult();
        var wrong = new List<string>();
        var checks = 0;

        foreach (var permission in probe.Catalogue.All.Where(p => p.RequiresStepUp))
        {
            foreach (var role in SystemRoles.All.Where(r => probe.GrantsOf(r.Key).Contains(permission.Key)))
            {
                if (Gap(role.Key, permission.Key) is not null)
                {
                    continue;
                }

                var freshly = await probe.ProbeAsync(role.Key, ProbeBranch.Own, permission.Key);
                var stalely = await probe.ProbeAsync(
                    role.Key, ProbeBranch.Own, permission.Key, SessionStrength.Stale);

                checks++;

                if (freshly != fresh || stalely != stale)
                {
                    wrong.Add(
                        $"{role.Key} exercising '{permission.Key}' was answered {freshly} on a fresh "
                        + $"session and {stalely} on a stale one; expected {fresh} and {stale}.");
                }
            }
        }

        checks.ShouldBeGreaterThan(0, "No step-up permission was exercised on both session states.");
        wrong.ShouldBeEmpty(string.Join("\n  ", wrong));
    }

    /// <summary>
    /// Every dimension the fixtures describe is one this suite drives. A dimension nobody exercises is
    /// an expectation nobody is checking, written where a reader will assume somebody is.
    /// </summary>
    [Fact]
    public void EveryDimensionInTheFixturesIsExercisedBySomeTest()
    {
        string[] driven =
        [
            "holder-own-branch", "holder-other-branch", "reach-other-branch",
            "wide-read-with-reach", "wide-read-without-reach",
            "second-branch-not-current", "second-branch-assigned-reach",
            "non-holder-own-branch",
            "no-session", "unknown-resource", "flagged-without-second-factor",
            "flagged-stale-step-up", "flagged-fresh-step-up",
        ];

        Fixtures.Dimensions.ShouldBe(driven, ignoreOrder: true);
    }

    private static OrganisationReachGap? Gap(string role, string permission)
        => Fixtures.OrganisationReachGaps.FirstOrDefault(gap =>
            string.Equals(gap.Role, role, StringComparison.Ordinal)
            && string.Equals(gap.Permission, permission, StringComparison.Ordinal));
}
