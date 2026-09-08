using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Modules.Customers.Contracts.Preferences;
using Tailor360.Modules.Customers.Domain.Preferences;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// The duplicate screen and the irreversible merge over HTTP: who may perform one, what it does to
/// both records, what it writes down, and everything it refuses afterwards.
/// </summary>
/// <remarks>
/// <para>
/// These are integration tests because almost nothing they assert is in-process. Two records are
/// locked in one transaction; the merge, its evidence and the outbox message commit together or not at
/// all; the append-only triggers and the check constraints are in the database; and
/// <c>customers.merge</c> is declared <c>RequiresMfa</c> and <c>RequiresStepUp</c>, which only the real
/// host evaluates. A substitute would answer all of it from a dictionary.
/// </para>
/// <para>
/// The rule under test throughout is <c>docs/prd/exceptions.md</c> EX-01: a merge is authorised, needs
/// a reason and a step-up, keeps the merged number searchable, re-points what Customers owns and
/// announces the rest — and cannot be undone.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class CustomerMergeEndpointTests(WebApplicationFixture fixture)
{
    // Branches of their own. IdentifierEditingTests holds …d1 and …d2, and CustomerHarness.BranchAsync
    // creates a branch only when it is missing, so sharing them would silently give these records
    // customer numbers carrying that class's branch code.
    private static readonly Guid FirstBranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000e1");
    private static readonly Guid SecondBranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000e2");

    private const string FirstBranchCode = "MRG1";
    private const string SecondBranchCode = "MRG2";

    private static readonly string[] SmsOnly = ["Sms"];

    /// <summary>
    /// One token per run, folded into every name these tests create.
    /// </summary>
    /// <remarks>
    /// The database is not dropped between runs, and CLAUDE.md section 5 asks for the suite to be run
    /// twice. Without this, the second run's first create would find the first run's record as a
    /// duplicate and be refused — a test that passes only once is a test that has told you nothing.
    /// </remarks>
    private static readonly string RunToken = AdministrationHarness.UniqueToken(8);

    /// <summary>
    /// The telephone number each pair shares, one per prefix and one set per run.
    /// </summary>
    /// <remarks>
    /// Shared within a pair, because a shared number is what scores <c>High</c> and makes the two
    /// records a duplicate at all. Distinct between prefixes, so two tests do not find each other's
    /// records and fail depending on the order they ran in.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, string> SharedPhones =
        new(StringComparer.Ordinal);

    private const string Reason =
        "Same phone number and address; she confirmed at the counter that the second record was "
        + "created when this branch could not see the first.";

    /* The merge itself -------------------------------------------------------------------------- */

    [Fact]
    public async Task MergingFoldsTheDuplicateIntoTheSurvivorAndKeepsItsNumberSearchable()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-happy", "203.0.113.180");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-happy");

        var merged = await MergeAsync(manager, survivor, duplicate);

        merged.StatusCode.ShouldBe(HttpStatusCode.OK);
        merged.Headers.ETag.ShouldNotBeNull();
        merged.Headers.CacheControl?.NoStore.ShouldBeTrue();

        var outcome = await ReadAsync<MergeBody>(merged);
        outcome.MergedCustomerId.ShouldBe(duplicate.CustomerId);
        outcome.MergedCustomerNumber.ShouldBe(duplicate.CustomerNumber);
        outcome.MergeId.ShouldNotBe(Guid.Empty);
        outcome.Customer.CustomerId.ShouldBe(survivor.CustomerId);
        outcome.Customer.MergedIntoCustomerId.ShouldBeNull();
        outcome.RecordsRepointed.ShouldBe(0);

        // The two records were written under different names, so both the number and the old name are
        // kept against the survivor.
        outcome.AliasesRecorded.ShouldBe(2);
        outcome.Customer.Aliases
            .ShouldContain(alias =>
                alias.Kind == "MergedCustomerNumber" && alias.Value == duplicate.CustomerNumber);

        // EX-01: "the merged number survives as an alias". Asserted by searching for it rather than by
        // reading the alias row, because being findable is the promise.
        var found = await SearchAsync(manager, duplicate.CustomerNumber);
        found.Customers.ShouldContain(card => card.CustomerId == survivor.CustomerId);
        found.Customers.ShouldNotContain(card => card.CustomerId == duplicate.CustomerId);

        var folded = await ReadCustomerAsync(
            await manager.GetAsync($"/api/v1/customers/{duplicate.CustomerId}"));

        folded.Status.ShouldBe("Deactivated");
        folded.MergedIntoCustomerId.ShouldBe(survivor.CustomerId);
        folded.MergedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task EveryBranchThatCouldSeeTheDuplicateCanSeeTheSurvivor()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // A merge that took the customer off a branch's screen would be a worse outcome than the
        // duplicate it ended, so the survivor inherits the visibility.
        using var manager = await ManagerAsync("mrg-visible", "203.0.113.181");
        var survivor = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-visible-a", phone: SharedPhone("visible"))));

        await CustomerHarness.BranchAsync(fixture, SecondBranchId, SecondBranchCode);

        using var second = await CustomerHarness.ManagerAsync(
            fixture, "mrg-visible2", "203.0.113.182", SecondBranchId, CustomerHarness.BranchManager);

        var duplicate = await ReadCustomerAsync(
            await CreateAsync(
                second,
                Registration("mrg-visible-b", phone: SharedPhone("visible"), reviewed: true)));

        survivor.VisibilityBranchIds.ShouldBe([FirstBranchId]);
        duplicate.VisibilityBranchIds.ShouldBe([SecondBranchId]);

        var outcome = await ReadAsync<MergeBody>(
            await MergeAsync(manager, survivor, duplicate));

        outcome.VisibilityBranchesAdded.ShouldBe(1);
        outcome.Customer.VisibilityBranchIds.ShouldBe(
            [FirstBranchId, SecondBranchId], ignoreOrder: true);
    }

    [Fact]
    public async Task AChainOfMergesIsFlattenedSoEveryPointerIsOneHopFromARecordThatStands()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // A merged into B, and B later merged into C. Without flattening, A would name a record that
        // itself no longer stands, and every reader would have to walk the chain and guard the cycle.
        using var manager = await ManagerAsync("mrg-chain", "203.0.113.183");
        var phone = SharedPhone("chain");

        var b = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-chain-b", phone: phone)));
        var a = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-chain-a", phone: phone, reviewed: true)));
        var c = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-chain-c", phone: phone, reviewed: true)));

        await MergeAsync(manager, b, a);

        var reloadedB = await ReadCustomerAsync(
            await manager.GetAsync($"/api/v1/customers/{b.CustomerId}"));

        var second = await ReadAsync<MergeBody>(await MergeAsync(manager, c, reloadedB));

        second.RecordsRepointed.ShouldBe(1);

        var flattened = await ReadCustomerAsync(
            await manager.GetAsync($"/api/v1/customers/{a.CustomerId}"));

        flattened.MergedIntoCustomerId.ShouldBe(c.CustomerId);
    }

    /* What it writes down ----------------------------------------------------------------------- */

    [Fact]
    public async Task MergingPublishesCustomerMergedIntoTheCustomersOutboxCarryingIdentifiersOnly()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-event", "203.0.113.184");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-event");

        var outcome = await ReadAsync<MergeBody>(
            await MergeAsync(manager, survivor, duplicate));

        var message = (await OutboxAsync(survivor.CustomerId))
            .SingleOrDefault(row => row.EventType == "customers.customer-merged.v1")
            .ShouldNotBeNull("The merge committed without the event that tells every other module.");

        message.SchemaVersion.ShouldBe(1);
        message.AggregateId.ShouldBe(survivor.CustomerId);

        var payload = JsonDocument.Parse(message.Payload).RootElement;
        payload.GetProperty("mergedCustomerId").GetGuid().ShouldBe(duplicate.CustomerId);
        payload.GetProperty("mergeId").GetGuid().ShouldBe(outcome.MergeId);
        payload.GetProperty("branchId").GetGuid().ShouldBe(FirstBranchId);

        // The negative half, and the reason this event is worth a test of its own. conventions.md
        // section 5.5 admits identifiers, codes, statuses, timestamps, amounts and branch codes; a
        // customer number is what she reads off a card and the reason is free text a member of staff
        // typed about her. Neither may fan out to every registered handler.
        var raw = message.Payload;
        raw.ShouldNotContain(duplicate.CustomerNumber, Case.Insensitive);
        raw.ShouldNotContain(survivor.CustomerNumber, Case.Insensitive);
        raw.ShouldNotContain(survivor.DisplayName, Case.Insensitive);
        raw.ShouldNotContain("Same phone number", Case.Insensitive);
    }

    [Fact]
    public async Task MergingWritesOneAuditEntryAgainstEachRecordAndNeitherCarriesAName()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-audit", "203.0.113.185");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-audit");

        await MergeAsync(manager, survivor, duplicate);

        var onSurvivor = (await AuditEntriesAsync(survivor.CustomerId))
            .SingleOrDefault(entry => entry.Action == "customers.customer.merged")
            .ShouldNotBeNull();

        var onDuplicate = (await AuditEntriesAsync(duplicate.CustomerId))
            .SingleOrDefault(entry => entry.Action == "customers.customer.merged-away")
            .ShouldNotBeNull();

        // The trail is read by entity, and a merge is the one change whose interesting answer for one
        // of the two records is only ever written against the other.
        onSurvivor.After.ShouldNotBeNull();
        JsonDocument.Parse(onSurvivor.After).RootElement
            .GetProperty("mergedWith").GetGuid().ShouldBe(duplicate.CustomerId);

        onDuplicate.After.ShouldNotBeNull();
        JsonDocument.Parse(onDuplicate.After).RootElement
            .GetProperty("mergedWith").GetGuid().ShouldBe(survivor.CustomerId);

        onSurvivor.After.ShouldNotContain(survivor.DisplayName, Case.Insensitive);
        onDuplicate.After.ShouldNotContain(duplicate.DisplayName, Case.Insensitive);
    }

    [Fact]
    public async Task MergingWritesTheDecisionAndTheEvidenceForIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-evidence", "203.0.113.186");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-evidence");

        var outcome = await ReadAsync<MergeBody>(
            await MergeAsync(manager, survivor, duplicate));

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();

        var record = (await context.CustomerMerges
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    merge => merge.Id == outcome.MergeId, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        record.SurvivorCustomerId.ShouldBe(survivor.CustomerId);
        record.MergedCustomerId.ShouldBe(duplicate.CustomerId);
        record.MergedCustomerNumber.ShouldBe(duplicate.CustomerNumber);
        record.Reason.ShouldBe(Reason);
        record.BranchId.ShouldBe(FirstBranchId);
        record.MergedBy.ShouldNotBeNull();

        // EX-01 asks for the duplicate suspicion "with score and explained reasons", so the decision
        // says what the two records had in common and not only that somebody merged them.
        var decision = (await context.DuplicateCandidates
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.MergeId == outcome.MergeId, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        decision.SubjectCustomerId.ShouldBe(survivor.CustomerId);
        decision.CandidateCustomerId.ShouldBe(duplicate.CustomerId);
        decision.Decision.ToString().ShouldBe("Merged");
        decision.Reasons.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task CreatingASecondRecordAnywayIsRecordedAsADecisionSomebodyTook()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // The other half of EX-01, and the one that is easy to forget: knowing that a person looked at
        // two records and judged them different people is what stops the pair being raised for ever.
        using var manager = await ManagerAsync("mrg-anyway", "203.0.113.187");
        var phone = SharedPhone("anyway");

        var first = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-anyway-a", phone: phone)));
        var second = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-anyway-b", phone: phone, reviewed: true)));

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();

        var decision = (await context.DuplicateCandidates
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.SubjectCustomerId == second.CustomerId,
                    TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        decision.CandidateCustomerId.ShouldBe(first.CustomerId);
        decision.Decision.ToString().ShouldBe("CreatedNewAnyway");
        decision.MergeId.ShouldBeNull();
        decision.Confidence.ToString().ShouldBe("High");
    }

    /* Who may do it ----------------------------------------------------------------------------- */

    [Fact]
    public async Task ReceptionMayReadTheDuplicatesAndMayNotMerge()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // raci.md note (1): "Reception is consulted-then-blocked rather than free to merge". Reading
        // who might be a duplicate is what she does before asking a manager, so the two endpoints are
        // gated differently on purpose.
        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);

        using var manager = await ManagerAsync("mrg-reception-m", "203.0.113.188");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-reception");

        using var reception = await CustomerHarness.CounterAsync(
            fixture, "mrg-reception", "203.0.113.189", FirstBranchId, CustomerHarness.Reception);

        var read = await reception.GetAsync($"/api/v1/customers/{survivor.CustomerId}/duplicates");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        var candidates = await ReadAsync<DuplicatesBody>(read);
        candidates.Candidates.ShouldContain(
            candidate => candidate.Customer.CustomerId == duplicate.CustomerId);

        var refused = await MergeAsync(reception, survivor, duplicate);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AManagerWhoHasNotReauthenticatedRecentlyCannotMerge()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // Holding customers.merge is not enough. The permission is declared RequiresMfa and
        // RequiresStepUp, and this client signed in and stopped there.
        using var enrolled = await ManagerAsync("mrg-stepup-m", "203.0.113.190");
        var (survivor, duplicate) = await PairAsync(enrolled, "mrg-stepup");

        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);

        using var unenrolled = await CustomerHarness.CounterAsync(
            fixture, "mrg-stepup", "203.0.113.191", FirstBranchId, CustomerHarness.BranchManager);

        var refused = await MergeAsync(unenrolled, survivor, duplicate);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARecordInAnotherOrganisationIsNotFoundRatherThanRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-tenant", "203.0.113.192");
        var (survivor, _) = await PairAsync(manager, "mrg-tenant");

        var elsewhere = await CustomerHarness.CustomerOfAnotherOrganisationAsync(fixture, FirstBranchId);

        var refused = await MergeAsync(manager, survivor, elsewhere);

        // "Not there" and "not yours" are one answer; telling them apart would be an oracle for
        // enumerating another organisation's records.
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /* The refusals ------------------------------------------------------------------------------ */

    [Fact]
    public async Task AMergeWithoutAReasonIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-reason", "203.0.113.193");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-reason");

        var refused = await MergeAsync(manager, survivor, duplicate, reason: "  ");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("customers.reason-required");
        await NotMergedAsync(manager, duplicate.CustomerId);
    }

    [Fact]
    public async Task AMergeWithoutAnIfMatchIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-precondition", "203.0.113.194");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-precondition");

        var refused = await manager.PostAsync(
            $"/api/v1/customers/{survivor.CustomerId}/merge",
            new { mergedCustomerId = duplicate.CustomerId, reason = Reason },
            Key());

        refused.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        await NotMergedAsync(manager, duplicate.CustomerId);
    }

    [Fact]
    public async Task AMergeAgainstAVersionSomebodyElseHasMovedOnIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-stale", "203.0.113.195");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-stale");

        // Somebody corrects the survivor between the manager reading it and deciding to merge.
        var corrected = await manager.PutAsync(
            $"/api/v1/customers/{survivor.CustomerId}",
            Correction(survivor, locality: "Gandhipuram"),
            [Key(), ("If-Match", $"\"{survivor.Version}\"")]);

        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refused = await MergeAsync(manager, survivor, duplicate);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await NotMergedAsync(manager, duplicate.CustomerId);
    }

    [Fact]
    public async Task ARecordCannotBeMergedIntoItself()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-self", "203.0.113.196");
        var (survivor, _) = await PairAsync(manager, "mrg-self");

        var refused = await MergeAsync(manager, survivor, survivor);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("customers.cannot-merge-into-itself");
        await NotMergedAsync(manager, survivor.CustomerId);
    }

    [Fact]
    public async Task ARecordThatHasBeenMergedIsFinished()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // One test for the whole shape of "there is no un-merge": it cannot be merged again, brought
        // back into use, opened at a branch, or written to.
        using var manager = await ManagerAsync("mrg-final", "203.0.113.197");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-final");

        await MergeAsync(manager, survivor, duplicate);

        var folded = await ReadCustomerAsync(
            await manager.GetAsync($"/api/v1/customers/{duplicate.CustomerId}"));

        var third = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-final-c")));

        // The problem code is asserted on every one of these, not only the status: a version conflict is
        // also a 409, and a test that accepted either would pass against a merge that had changed
        // nothing but the ETag.
        await RefusedAsMergedAsync(
            MergeAsync(manager, third, folded), "customers.already-merged");

        await RefusedAsMergedAsync(
            manager.PostAsync(
                $"/api/v1/customers/{folded.CustomerId}/reactivate",
                new { reason = "Asked for the old record back." },
                Key(), ("If-Match", $"\"{folded.Version}\"")),
            "customers.already-merged");

        await RefusedAsMergedAsync(
            manager.PostAsync($"/api/v1/customers/{folded.CustomerId}/open", new { }, Key()),
            "customers.already-merged");

        var purpose = await CustomerHarness.PurposeAsync(fixture);

        await RefusedAsMergedAsync(
            manager.PostAsync(
                $"/api/v1/customers/{folded.CustomerId}/consent",
                new { purposeKey = purpose, decision = "Granted", source = "counter, verbal" },
                Key()),
            "customers.already-merged");

        await RefusedAsMergedAsync(
            manager.PutAsync(
                $"/api/v1/customers/{folded.CustomerId}/communication-preferences",
                new
                {
                    allowedChannels = SmsOnly,
                    language = (string?)null,
                    quietHoursStart = (string?)null,
                    quietHoursEnd = (string?)null,
                },
                Key()),
            "customers.already-merged");
    }

    [Fact]
    public async Task AMergeThatNamesNoSecondRecordIsAMissingFieldRatherThanAMissingRecord()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-empty", "203.0.113.200");
        var (survivor, _) = await PairAsync(manager, "mrg-empty");

        var refused = await MergeAsync(manager, survivor, Guid.Empty);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(refused)).ShouldBe("customers.value-required");
    }

    [Fact]
    public async Task TheFirstMergedNumberStaysSearchableAfterTheSecondMerge()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // A merged into B, then B merged into C. A's number is an alias on B, and the search only
        // matches aliases on records it can see — B is now deactivated. Unless the survivor takes the
        // aliases over, EX-01's promise about an old receipt holds for one merge and not for two.
        using var manager = await ManagerAsync("mrg-carry", "203.0.113.202");
        var phone = SharedPhone("carry");

        var b = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-carry-b", phone: phone)));
        var a = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-carry-a", phone: phone, reviewed: true)));
        var c = await ReadCustomerAsync(
            await CreateAsync(manager, Registration("mrg-carry-c", phone: phone, reviewed: true)));

        await MergeAsync(manager, b, a);

        var reloadedB = await ReadCustomerAsync(
            await manager.GetAsync($"/api/v1/customers/{b.CustomerId}"));

        (await SearchAsync(manager, a.CustomerNumber)).Customers
            .ShouldContain(card => card.CustomerId == b.CustomerId);

        await MergeAsync(manager, c, reloadedB);

        // Both numbers now find C, which is the only record that still stands.
        foreach (var number in new[] { a.CustomerNumber, b.CustomerNumber })
        {
            (await SearchAsync(manager, number)).Customers
                .ShouldContain(
                    card => card.CustomerId == c.CustomerId,
                    $"{number} no longer finds the record that survived.");
        }
    }

    /// <summary>
    /// The send-time queries answer for the record that survived, not the one that was folded in.
    /// </summary>
    /// <remarks>
    /// EX-01: "After a merge the survivor's consent and channel preferences govern every later
    /// message." A notification queued before the merge is still holding the old identifier, and it
    /// evaluates consent at send time — so if these queries answered from the folded record, a
    /// withdrawal recorded on the survivor would be ignored and a message would go out that the
    /// person had said no to. The write path refusing a merged record is not enough on its own.
    /// </remarks>
    [Fact]
    public async Task ConsentAndPreferencesAnswerForTheSurvivorWhenAskedByTheOldIdentifier()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-consent", "203.0.113.203");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-consent");
        var purpose = await CustomerHarness.PurposeAsync(fixture);

        // She agreed at one counter and withdrew at the other; the records were then found to be one
        // person, and the survivor's answer is the one that stands.
        await RecordConsentAsync(manager, duplicate.CustomerId, purpose, "Granted");
        await RecordConsentAsync(manager, survivor.CustomerId, purpose, "Withdrawn");

        await CustomerHarness.PreferenceAsync(
            fixture, duplicate.CustomerId, [CommunicationChannel.Sms]);
        await CustomerHarness.PreferenceAsync(fixture, survivor.CustomerId, []);

        await MergeAsync(manager, survivor, duplicate);

        using var scope = fixture.Services.CreateScope();

        var consent = await scope.ServiceProvider.GetRequiredService<IConsentQuery>()
            .GetAsync(duplicate.CustomerId, purpose, TestContext.Current.CancellationToken);

        consent.Status.ShouldBe(ConsentStatus.Withdrawn);
        consent.CustomerId.ShouldBe(survivor.CustomerId);

        var preference = await scope.ServiceProvider
            .GetRequiredService<ICommunicationPreferenceQuery>()
            .GetAsync(duplicate.CustomerId, TestContext.Current.CancellationToken);

        preference.CustomerId.ShouldBe(survivor.CustomerId);
        preference.AllowedChannels.ShouldBeEmpty();
    }

    /* The duplicate screen ---------------------------------------------------------------------- */

    [Fact]
    public async Task TheDuplicateScreenNeverOffersTheRecordItselfOrOneAlreadyMerged()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-screen", "203.0.113.198");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-screen");

        var before = await DuplicatesAsync(manager, survivor.CustomerId);
        before.Candidates.ShouldNotContain(
            candidate => candidate.Customer.CustomerId == survivor.CustomerId);
        before.Candidates.ShouldContain(
            candidate => candidate.Customer.CustomerId == duplicate.CustomerId);

        await MergeAsync(manager, survivor, duplicate);

        // Offering it would send somebody to open a record that no longer stands, and merging into it
        // is refused anyway.
        var after = await DuplicatesAsync(manager, survivor.CustomerId);
        after.Candidates.ShouldNotContain(
            candidate => candidate.Customer.CustomerId == duplicate.CustomerId);

        // And a merged record has no candidates of its own, because nothing could be done with them.
        (await DuplicatesAsync(manager, duplicate.CustomerId)).Candidates.ShouldBeEmpty();
    }

    /// <summary>
    /// A candidate card says whether the caller can already see the record.
    /// </summary>
    /// <remarks>
    /// The same question the search answers, answered the same way. A card that always said "not
    /// yours" would have a client offering to open a record that is already on the screen, and would
    /// misdescribe the one case a merge screen exists to resolve — the same person, twice, once here.
    /// </remarks>
    [Fact]
    public async Task ACandidateCardSaysWhetherTheCallerCanAlreadySeeIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-cards", "203.0.113.204");
        var (survivor, duplicate) = await PairAsync(manager, "mrg-cards");

        CandidateFor(await DuplicatesAsync(manager, survivor.CustomerId), duplicate, "the owning branch")
            .Customer.VisibleToCaller.ShouldBeTrue();

        // The same pair read from a branch that has never served either of them.
        await CustomerHarness.BranchAsync(fixture, SecondBranchId, SecondBranchCode);

        using var elsewhere = await CustomerHarness.CounterAsync(
            fixture, "mrg-cards2", "203.0.113.205", SecondBranchId, CustomerHarness.Reception);

        CandidateFor(await DuplicatesAsync(elsewhere, survivor.CustomerId), duplicate, "another branch")
            .Customer.VisibleToCaller.ShouldBeFalse();
    }

    /// <summary>
    /// Picks one candidate out of a screen, saying what the screen held when it is not there.
    /// </summary>
    /// <remarks>
    /// <c>Single</c> on its own answers "sequence contains no matching element", which says nothing
    /// about what the caller actually saw. This list is assembled from a scored query over shared
    /// state, so when it surprises somebody the useful evidence is the list itself.
    /// </remarks>
    private static CandidateBody CandidateFor(
        DuplicatesBody screen,
        CustomerBody wanted,
        string readAs)
    {
        var found = screen.Candidates
            .SingleOrDefault(candidate => candidate.Customer.CustomerId == wanted.CustomerId);

        return found.ShouldNotBeNull(
            $"The duplicate {wanted.CustomerNumber} ({wanted.CustomerId}) was not offered when the "
            + $"screen was read from {readAs}. It held "
            + (screen.Candidates.Count == 0
                ? "nothing at all."
                : string.Join(
                    ", ",
                    screen.Candidates.Select(candidate =>
                        $"{candidate.Customer.CustomerNumber}/{candidate.Confidence}"
                        + $"/visible={candidate.Customer.VisibleToCaller}"))));
    }

    /// <summary>
    /// A shared name and a shared place, with no shared telephone number, score
    /// <c>Medium</c> — which is the threshold that blocks a create.
    /// </summary>
    /// <remarks>
    /// This is a regression test with a story. The duplicate query used to project only the name and
    /// the primary number and pass null for the address, so <c>SameLocality</c> and
    /// <c>SamePostcode</c> could never fire and <c>DuplicateConfidence.Medium</c> was unreachable
    /// through the endpoint. Every "same name, same place" pair scored <c>Low</c>, the create screen
    /// never blocked on one, and EX-01's inline warning was quietly weaker than the rule it
    /// implements. The unit tests passed throughout, because the scoring was never wrong.
    /// </remarks>
    [Fact]
    public async Task ASharedNameAndASharedPlaceAreEnoughToStopACreate()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var manager = await ManagerAsync("mrg-medium", "203.0.113.199");

        // Two spellings of one name, folded to the same key by the normaliser, at the same postcode
        // and with different telephone numbers. The run token is on both and folds identically, so it
        // isolates the pair from earlier runs without changing what the score sees.
        var first = await ReadCustomerAsync(await CreateAsync(
            manager,
            Registration("mrg-medium-a", displayName: $"Bhuvaneshwari Karthik {RunToken}")));

        var blocked = await CreateAsync(
            manager,
            Registration("mrg-medium-b", displayName: $"Buvaneswari Kartik {RunToken}"));

        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = JsonDocument.Parse(
            await blocked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        problem.GetProperty("type").GetString().ShouldNotBeNull()
            .ShouldContain("duplicates-not-reviewed");

        var candidates = problem.GetProperty("candidates");
        candidates.GetArrayLength().ShouldBe(1);
        candidates[0].GetProperty("confidence").GetString().ShouldBe("Medium");
        candidates[0].GetProperty("customer").GetProperty("customerId").GetGuid()
            .ShouldBe(first.CustomerId);

        var reasons = candidates[0].GetProperty("reasons").EnumerateArray()
            .Select(reason => reason.GetString()).ToList();

        reasons.ShouldContain("SameFoldedName");
        reasons.ShouldContain("SamePostcode");
    }

    /* Helpers ----------------------------------------------------------------------------------- */

    private async Task<AuthenticationClient> ManagerAsync(string prefix, string clientAddress)
    {
        await CustomerHarness.BranchAsync(fixture, FirstBranchId, FirstBranchCode);

        return await CustomerHarness.ManagerAsync(
            fixture, prefix, clientAddress, FirstBranchId, CustomerHarness.BranchManager);
    }

    /// <summary>
    /// Two records for one person, sharing a telephone number so they score <c>High</c>.
    /// </summary>
    /// <remarks>
    /// The second is created with <c>duplicatesReviewed</c>, which is exactly the state EX-01
    /// describes: somebody read the candidates, judged wrongly, and left the shop with two records.
    /// </remarks>
    private static async Task<(CustomerBody Survivor, CustomerBody Duplicate)> PairAsync(
        AuthenticationClient client,
        string prefix)
    {
        var phone = SharedPhone(prefix);

        var survivor = await ReadCustomerAsync(
            await CreateAsync(client, Registration($"{prefix}-a", phone: phone)));

        var duplicate = await ReadCustomerAsync(
            await CreateAsync(
                client,
                Registration($"{prefix}-b", phone: phone, reviewed: true)));

        return (survivor, duplicate);
    }

    private static Task<HttpResponseMessage> MergeAsync(
        AuthenticationClient client,
        CustomerBody survivor,
        CustomerBody merged,
        string? reason = Reason)
        => MergeAsync(client, survivor, merged.CustomerId, reason);

    private static Task<HttpResponseMessage> MergeAsync(
        AuthenticationClient client,
        CustomerBody survivor,
        Guid mergedCustomerId,
        string? reason = Reason)
        => client.PostAsync(
            $"/api/v1/customers/{survivor.CustomerId}/merge",
            new { mergedCustomerId, reason },
            Key(),
            ("If-Match", $"\"{survivor.Version}\""));

    private static async Task<DuplicatesBody> DuplicatesAsync(
        AuthenticationClient client,
        Guid customerId)
    {
        var response = await client.GetAsync($"/api/v1/customers/{customerId}/duplicates");

        // The body on a refusal, not only the status: a 403 and a 429 are the same number of
        // characters to read and completely different problems to chase.
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        response.Headers.CacheControl?.NoStore.ShouldBeTrue();

        return await ReadAsync<DuplicatesBody>(response);
    }

    /// <summary>Asserts a 409 refusal carrying the code that says the record has been merged.</summary>
    private static async Task RefusedAsMergedAsync(Task<HttpResponseMessage> attempt, string code)
    {
        var response = await attempt;

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthenticationClient.CodeAsync(response)).ShouldBe(code);
    }

    /// <summary>Asserts that a refused merge left the record standing.</summary>
    private static async Task NotMergedAsync(AuthenticationClient client, Guid customerId)
    {
        var customer = await ReadCustomerAsync(await client.GetAsync($"/api/v1/customers/{customerId}"));

        customer.MergedIntoCustomerId.ShouldBeNull();
        customer.Status.ShouldBe("Active");
    }

    private static Task<HttpResponseMessage> CreateAsync(
        AuthenticationClient client,
        RegistrationBody registration)
        => client.PostAsync("/api/v1/customers/", registration, Key());

    private static async Task RecordConsentAsync(
        AuthenticationClient client,
        Guid customerId,
        string purposeKey,
        string decision)
        => (await client.PostAsync(
                $"/api/v1/customers/{customerId}/consent",
                new { purposeKey, decision, source = "counter, verbal" },
                Key()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

    private static async Task<PageBody> SearchAsync(AuthenticationClient client, string term)
    {
        var response = await client.GetAsync(
            $"/api/v1/customers/?term={Uri.EscapeDataString(term)}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ReadAsync<PageBody>(response);
    }

    private static async Task<CustomerBody> ReadCustomerAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        return await ReadAsync<CustomerBody>(response);
    }

    private static async Task<TBody> ReadAsync<TBody>(HttpResponseMessage response)
        where TBody : class
        => (await AuthenticationClient.ReadAsync<TBody>(response)).ShouldNotBeNull();

    private async Task<IReadOnlyList<OutboxRow>> OutboxAsync(Guid customerId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomersDbContext>();

        return
        [
            .. await context.OutboxMessages
                .Where(message => message.AggregateId == customerId)
                .OrderBy(message => message.OccurredAt)
                .ThenBy(message => message.Id)
                .Select(message => new OutboxRow(
                    message.EventType,
                    message.SchemaVersion,
                    message.AggregateId,
                    message.Payload))
                .ToListAsync(TestContext.Current.CancellationToken),
        ];
    }

    private async Task<IReadOnlyList<AuditRow>> AuditEntriesAsync(Guid customerId)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        return
        [
            .. await context.AuditEvents
                .Where(entry => entry.EntityId == customerId)
                .OrderBy(entry => entry.Sequence)
                .Select(entry => new AuditRow(entry.Action, entry.Summary, entry.Before, entry.After))
                .ToListAsync(TestContext.Current.CancellationToken),
        ];
    }

    /// <summary>A fresh idempotency key, which every command on this surface requires.</summary>
    private static (string Name, string Value) Key()
        => ("Idempotency-Key", Guid.CreateVersion7().ToString());

    /// <summary>One number for both records of a pair, and a different one for every test.</summary>
    /// <param name="prefix">The test's own prefix.</param>
    /// <returns>The number.</returns>
    private static string SharedPhone(string prefix)
        => SharedPhones.GetOrAdd(prefix, _ => CustomerHarness.UniquePhone());

    private static RegistrationBody Registration(
        string prefix,
        string? displayName = null,
        string? phone = null,
        bool reviewed = false)
        => new(
            displayName ?? $"Kavitha {prefix} {RunToken}",
            null,
            phone ?? CustomerHarness.UniquePhone(),
            null,
            $"{prefix}@example.invalid",
            "12 Second Street, Demo Nagar",
            "Peelamedu",
            "641004",
            "ta-IN",
            reviewed);

    private static CorrectionBody Correction(CustomerBody customer, string locality)
        => new(
            customer.DisplayName,
            customer.NativeName,
            customer.Phone,
            customer.AlternatePhone,
            customer.Email,
            customer.AddressLine,
            locality,
            customer.Postcode,
            customer.Language,
            "Confirmed the area with the customer at the counter.");

    private sealed record RegistrationBody(
        string? DisplayName,
        string? NativeName,
        string? Phone,
        string? AlternatePhone,
        string? Email,
        string? AddressLine,
        string? Locality,
        string? Postcode,
        string? Language,
        bool DuplicatesReviewed);

    private sealed record CorrectionBody(
        string? DisplayName,
        string? NativeName,
        string? Phone,
        string? AlternatePhone,
        string? Email,
        string? AddressLine,
        string? Locality,
        string? Postcode,
        string? Language,
        string? Reason);

    private sealed record CustomerBody(
        Guid CustomerId,
        string CustomerNumber,
        string DisplayName,
        string? NativeName,
        string? Phone,
        string? AlternatePhone,
        string? Email,
        string? AddressLine,
        string? Locality,
        string? Postcode,
        string Language,
        string Status,
        Guid OwningBranchId,
        IReadOnlyList<Guid> VisibilityBranchIds,
        IReadOnlyList<AliasBody> Aliases,
        string Version,
        Guid? MergedIntoCustomerId,
        DateTimeOffset? MergedAt);

    private sealed record AliasBody(string Kind, string Value);

    private sealed record MergeBody(
        CustomerBody Customer,
        Guid MergeId,
        Guid MergedCustomerId,
        string MergedCustomerNumber,
        int AliasesRecorded,
        int VisibilityBranchesAdded,
        int RecordsRepointed,
        DateTimeOffset MergedAt);

    private sealed record DuplicatesBody(IReadOnlyList<CandidateBody> Candidates);

    private sealed record CandidateBody(
        CardBody Customer,
        string Confidence,
        IReadOnlyList<string> Reasons);

    private sealed record CardBody(
        Guid CustomerId,
        string CustomerNumber,
        string DisplayName,
        string MaskedPhone,
        Guid OwningBranchId,
        bool VisibleToCaller,
        string Status);

    private sealed record PageBody(IReadOnlyList<CardBody> Customers, string? NextCursor);

    private sealed record AuditRow(string Action, string Summary, string? Before, string? After);

    private sealed record OutboxRow(
        string EventType,
        int SchemaVersion,
        Guid AggregateId,
        string Payload);
}
