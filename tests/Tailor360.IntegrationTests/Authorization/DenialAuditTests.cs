using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Tailor360.Modules.Identity.Application.Access;
using Tailor360.Platform.Persistence.Entities;
using Tailor360.Platform.Security.Audit;
using Tailor360.Platform.Security.Authorisation;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Authorization;

/// <summary>
/// Refused attempts reaching the audit trail: which ones, how often, and what they are allowed to say.
/// </summary>
/// <remarks>
/// <para>
/// A denial trail exists so that a pattern is visible — one person trying the same forbidden action all
/// afternoon, or forty accounts trying it in a minute. That makes two properties load-bearing, and they
/// pull in opposite directions. It must not write forty identical rows for one person's retrying
/// client, and it must never decide that some denials are not worth writing: the eleventh actor refused
/// in a minute is exactly the one somebody will go looking for. Coalescing on actor, endpoint and minute
/// is what satisfies both, and these tests hold it to that.
/// </para>
/// <para>
/// The clock in the probe application does not move, so every request in this class falls in one minute.
/// That is the hardest case for coalescing rather than the easiest: everything collides, and only the
/// key distinguishes. It is also why these tests act as the probe application's second cohort — the
/// people seeded in the other branch, whom the matrix tests never act as. Sharing actors with those
/// tests would mean a refusal here was collapsed into one of theirs, and every assertion below would be
/// about somebody else's entry.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(AuthorisationProbeCollection.Name)]
public sealed class DenialAuditTests(AuthorisationProbeApplication probe)
{
    private static readonly MatrixFixtures Fixtures = MatrixFixtures.Load();

    /// <summary>The action a refusal is recorded under is the one the fixtures name.</summary>
    [Fact]
    public void TheFixturesAndTheRecorderAgreeOnTheAction()
        => Fixtures.DenialAuditAction.ShouldBe(AuthorisationDenialAuditingHandler.Action);

    /// <summary>
    /// A refused attempt to change something is recorded, and the entry says who, where and why without
    /// saying anything about what they sent.
    /// </summary>
    [Fact]
    public async Task ARefusedWriteIsRecordedWithTheActorTheEndpointAndNothingElse()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        const string Permission = CustomersPermissions.Merge;
        var refused = await WriteInOwnBranch(SystemRoles.Tailor, Permission);

        refused.ShouldBe(Fixtures.Expected("non-holder-own-branch").AsResult());

        var entries = await EntriesFor(Permission);
        var entry = entries.ShouldHaveSingleItem();

        entry.Action.ShouldBe(AuthorisationDenialAuditingHandler.Action);
        entry.EntityType.ShouldBe(AuthorisationDenialAuditingHandler.EntityType);
        entry.ActorId.ShouldBe(probe.UserIdOf(SystemRoles.Tailor, ProbeBranch.Other));

        // The route template, never the request path: the path carries the identifier the caller chose,
        // and the summary is a string a query groups by.
        entry.Summary.ShouldContain($"POST /probe-write/{Permission}/{{branchId}}");
        entry.Summary.ShouldContain("PermissionNotHeld");
        entry.Summary.ShouldNotContain(probe.OtherBranchId.ToString());

        // Nothing about the attempt's contents. A refused request is unvalidated input, and this table
        // is append-only.
        entry.Before.ShouldBeNull();
        entry.After.ShouldBeNull();
    }

    /// <summary>
    /// A refused read is not recorded. A client finds out what it may do by asking, and a work queue
    /// that refuses to show another branch's jobs would otherwise write an entry every time somebody
    /// opened a screen — noise, and in aggregate a record of what people looked at.
    /// </summary>
    [Fact]
    public async Task ARefusedReadIsNotRecorded()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        const string Permission = CustomersPermissions.ReadNotes;

        var before = await CountFor(Permission, write: false);
        await probe.ProbeAsSecondCohortAsync(SystemRoles.Tailor, ProbeBranch.Other, Permission);
        var after = await CountFor(Permission, write: false);

        after.ShouldBe(before);
    }

    /// <summary>
    /// A read is recorded after all when the endpoint demands a fresh re-authentication. Somebody
    /// probing an action that needs step-up is doing something worth seeing, whichever verb it uses.
    /// </summary>
    [Fact]
    public async Task ARefusedStepUpRequestIsRecordedEvenThoughItIsARead()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        const string Permission = CustodyPermissions.GenerateIdentity;
        probe.Catalogue.Find(Permission)!.RequiresStepUp.ShouldBeTrue();

        var refused = await probe.ProbeAsSecondCohortAsync(
            SystemRoles.BranchManager, ProbeBranch.Other, Permission, SessionStrength.Stale);

        refused.ShouldBe(Fixtures.Expected("flagged-stale-step-up").AsResult());

        var entries = await EntriesForRead(Permission);
        entries.ShouldContain(entry =>
            entry.ActorId == probe.UserIdOf(SystemRoles.BranchManager, ProbeBranch.Other)
            && entry.Summary.Contains("StepUpRequired", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ten refusals by one person at one endpoint in one minute are one entry. The tenth is the same
    /// event as the first, and nine copies of it would bury whatever else happened that minute.
    /// </summary>
    [Fact]
    public async Task RepeatedRefusalsByOnePersonAtOneEndpointInOneMinuteAreOneEntry()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        const string Permission = BillingPermissions.PostCreditNote;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await WriteInOwnBranch(SystemRoles.Tailor, Permission);
        }

        (await EntriesFor(Permission)).Count.ShouldBe(1);
    }

    /// <summary>
    /// The other half, and the one that matters: a different person refused at the same endpoint in the
    /// same minute is a different entry. Nothing is dropped because enough has already been written.
    /// </summary>
    [Fact]
    public async Task EveryDistinctPersonRefusedAtOneEndpointInOneMinuteIsRecorded()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        const string Permission = InventoryPermissions.ApproveVariance;

        // Every role that does not hold it, which is most of them, each refused twice.
        var refusedRoles = SystemRoles.All
            .Where(role => !probe.GrantsOf(role.Key).Contains(Permission))
            .Select(role => role.Key)
            .ToArray();

        refusedRoles.Length.ShouldBeGreaterThan(5, "Too few roles are refused for this to prove anything.");

        foreach (var role in refusedRoles.Concat(refusedRoles))
        {
            await WriteInOwnBranch(role, Permission);
        }

        var entries = await EntriesFor(Permission);

        entries.Count.ShouldBe(refusedRoles.Length);
        entries.Select(entry => entry.ActorId).Distinct().Count().ShouldBe(refusedRoles.Length);
    }

    /// <summary>
    /// The same person refused at two endpoints in one minute is two entries. Coalescing is about one
    /// event repeating, not about how much one person may do wrong per minute.
    /// </summary>
    [Fact]
    public async Task OnePersonRefusedAtTwoEndpointsInOneMinuteIsTwoEntries()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        await WriteInOwnBranch(SystemRoles.Tailor, BillingPermissions.Refund);
        await WriteInOwnBranch(SystemRoles.Tailor, BillingPermissions.Reverse);

        (await EntriesFor(BillingPermissions.Refund)).ShouldHaveSingleItem();
        (await EntriesFor(BillingPermissions.Reverse)).ShouldHaveSingleItem();
    }

    /// <summary>
    /// A request refused for naming another branch's record is recorded too. It is the shape of somebody
    /// walking identifiers, and it is a state-changing request whatever the refusal was.
    /// </summary>
    [Fact]
    public async Task AWriteRefusedForNamingAnotherBranchIsRecorded()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        const string Permission = OrdersPermissions.Cancel;

        var refused = await probe.ProbeAsSecondCohortAsync(
            SystemRoles.BranchManager, ProbeBranch.Own, Permission, write: true);
        refused.ShouldBe(Fixtures.Expected("holder-other-branch").AsResult());

        var entry = (await EntriesFor(Permission)).ShouldHaveSingleItem();
        entry.Summary.ShouldContain("ResourceUnreachable");

        // The status recorded is the status served. A cross-branch attempt is answered 404 on purpose,
        // and an entry saying 403 would put the trail at odds with the wire on exactly the requests an
        // investigation reads first.
        entry.Summary.ShouldContain(((int)refused.Status).ToString(CultureInfo.InvariantCulture));
        entry.Summary.ShouldNotContain("403");
    }

    /// <summary>
    /// A refusal that is purely about the branch names the branch, not the permission. The person here
    /// holds what the endpoint demands and is refused for reaching outside the branch they work in, and
    /// an auditor reading a flood of denials needs those two told apart.
    /// </summary>
    [Fact]
    public async Task AWriteRefusedForTheCallersBranchScopeIsRecordedAsABranchRefusal()
    {
        Assert.SkipUnless(AuthorisationProbeApplication.IsAvailable, DatabaseAvailability.SkipReason);

        // An organisation-scoped permission the role holds, exercised by a role with no organisation
        // reach: the caller-side branch requirement is the thing that refuses, and it is the only
        // requirement that does.
        const string Permission = PlatformPermissions.FeatureFlags;
        probe.GrantsOf(SystemRoles.HyFibSuperUser).ShouldContain(Permission);

        var refused = await probe.ProbeAsSecondCohortAsync(
            SystemRoles.HyFibSuperUser, ProbeBranch.Other, Permission, write: true);

        // The answer is the recorded organisation-reach gap's own: this is the vendor principal and
        // admin.feature_flags, the one place matrix.yaml records approval and enforcement disagreeing.
        refused.ShouldBe(Fixtures.Answer("not-permitted").AsResult());

        var entry = (await EntriesFor(Permission)).ShouldHaveSingleItem();
        entry.Summary.ShouldContain(nameof(AuthorisationRefusal.OutsideBranchScope));
        entry.Summary.ShouldNotContain(nameof(AuthorisationRefusal.PermissionNotHeld));
        entry.Summary.ShouldContain(((int)refused.Status).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A refused write by a second-cohort person against their own branch.</summary>
    private Task<ProbeResult> WriteInOwnBranch(string role, string permission)
        => probe.ProbeAsSecondCohortAsync(role, ProbeBranch.Other, permission, write: true);

    private Task<List<AuditEvent>> EntriesFor(string permission)
        => EntriesFor(AuthorisationProbeApplication.WriteProbeAuditIdentifier(permission));

    private Task<List<AuditEvent>> EntriesForRead(string permission)
        => EntriesFor(AuthorisationDenialAuditingHandler.IdentifierFor(
            $"GET /probe/{permission}/{{branchId}}"));

    private async Task<List<AuditEvent>> EntriesFor(Guid entityId)
    {
        await using var context = probe.OpenPlatformContext();

        return await context.AuditEvents
            .AsNoTracking()
            .Where(entry => entry.Action == AuthorisationDenialAuditingHandler.Action
                            && entry.EntityId == entityId)
            .OrderBy(entry => entry.Sequence)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountFor(string permission, bool write)
    {
        var entityId = write
            ? AuthorisationProbeApplication.WriteProbeAuditIdentifier(permission)
            : AuthorisationDenialAuditingHandler.IdentifierFor($"GET /probe/{permission}/{{branchId}}");

        return (await EntriesFor(entityId)).Count;
    }
}
