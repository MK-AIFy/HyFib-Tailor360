using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Modules.Customers.Contracts.Preferences;
using Tailor360.Modules.Customers.Infrastructure.Persistence;
using Tailor360.Platform.Persistence.Contexts;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// Consent and communication preferences over HTTP: recording an answer, changing it, and the
/// refusals that keep the record evidence rather than a setting.
/// </summary>
/// <remarks>
/// These run against a real PostgreSQL because the guarantees under test are the database's: the
/// trigger that refuses to let a consent row be edited, the <c>xmin</c> token two counters race on,
/// and the ordering the standing answer is computed from.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class ConsentEndpointTests(WebApplicationFixture fixture)
{
    private static readonly Guid BranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000c7");

    private const string BranchCode = "CUST7";

    /* Recording an answer ------------------------------------------------------------------------ */

    [Fact]
    public async Task RecordingAnAnswerKeepsTheWordingVersionItWasGivenUnder()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-record", "203.0.113.140");
        var customerId = await CustomerAsync();
        var purpose = await CustomerHarness.PurposeAsync(fixture);

        var response = await RecordAsync(counter, customerId, purpose, "Granted", "counter, verbal");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var answer = await ReadAsync<AnswerBody>(response);
        answer.PurposeKey.ShouldBe(purpose);
        answer.Decision.ShouldBe("Granted");
        answer.WordingVersion.ShouldBe(1);
        answer.Source.ShouldBe("counter, verbal");
        answer.BranchId.ShouldBe(BranchId);
        answer.RecordId.ShouldNotBe(Guid.Empty);

        // What the endpoint said and what another module is told must be the same answer.
        var state = await ConsentAsync(customerId, purpose);
        state.Status.ShouldBe(ConsentStatus.Granted);
        state.RecordId.ShouldBe(answer.RecordId);
        state.WordingVersion.ShouldBe(1);
    }

    /// <summary>
    /// The whole reason the table is append-only. She agreed, she withdrew, and the row that recorded
    /// the agreement is still there saying what it always said.
    /// </summary>
    [Fact]
    public async Task WithdrawingAppendsAnAnswerAndLeavesTheOneItReplaces()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-withdraw", "203.0.113.141");
        var customerId = await CustomerAsync();
        var purpose = await CustomerHarness.PurposeAsync(fixture);

        var granted = await ReadAsync<AnswerBody>(
            await RecordAsync(counter, customerId, purpose, "Granted", "counter, verbal"));

        (await RecordAsync(counter, customerId, purpose, "Withdrawn", "telephone"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var consent = await ReadConsentAsync(counter, customerId);
        var state = consent.Purposes.Single(entry => entry.Key == purpose);

        state.Status.ShouldBe("Withdrawn");
        state.Answers.Count.ShouldBe(2);
        state.Answers[0].Decision.ShouldBe("Withdrawn");
        state.Answers[1].Decision.ShouldBe("Granted");
        state.Answers[1].RecordId.ShouldBe(granted.RecordId);

        (await ConsentAsync(customerId, purpose)).Status.ShouldBe(ConsentStatus.Withdrawn);
    }

    /// <summary>
    /// The refusal <c>init-reference-data</c> is built around: it seeds the five purposes and
    /// publishes no wording, because inventing the words a customer is read is exactly what the
    /// classification document forbids. Until an Owner publishes them, nothing can be consented to.
    /// </summary>
    [Fact]
    public async Task APurposeWithNoPublishedWordingCannotBeAnswered()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-nowording", "203.0.113.142");
        var customerId = await CustomerAsync();
        var purpose = await CustomerHarness.PurposeAsync(fixture, withWording: false);

        var response = await RecordAsync(counter, customerId, purpose, "Granted", "counter, verbal");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthenticationClient.CodeAsync(response))
            .ShouldBe("customers.consent-wording-not-published");

        var consent = await ReadConsentAsync(counter, customerId);
        var state = consent.Purposes.Single(entry => entry.Key == purpose);

        state.CanBeAnswered.ShouldBeFalse();
        state.CurrentWordingVersion.ShouldBe(0);
        state.Status.ShouldBe("NeverAsked");
    }

    [Fact]
    public async Task ARetiredPurposeIsNotAskedAboutAgain()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-retired", "203.0.113.143");
        var customerId = await CustomerAsync();
        var purpose = await CustomerHarness.PurposeAsync(fixture, retired: true);

        var response = await RecordAsync(counter, customerId, purpose, "Granted", "counter, verbal");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthenticationClient.CodeAsync(response)).ShouldBe("customers.consent-purpose-retired");
    }

    [Fact]
    public async Task AnAnswerAgainstAPurposeTheRegisterDoesNotCarryIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-unknown", "203.0.113.144");
        var customerId = await CustomerAsync();

        var response = await RecordAsync(
            counter, customerId, "no_such_purpose_here", "Granted", "counter, verbal");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthenticationClient.CodeAsync(response))
            .ShouldBe("customers.consent-purpose-not-found");
    }

    [Fact]
    public async Task AnOutcomeThatIsNoneOfTheThreeIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-decision", "203.0.113.145");
        var customerId = await CustomerAsync();
        var purpose = await CustomerHarness.PurposeAsync(fixture);

        var response = await RecordAsync(counter, customerId, purpose, "Maybe", "counter, verbal");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(response))
            .ShouldBe("customers.consent-decision-not-understood");
    }

    [Fact]
    public async Task AnAnswerWithNoSourceIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-source", "203.0.113.146");
        var customerId = await CustomerAsync();
        var purpose = await CustomerHarness.PurposeAsync(fixture);

        var response = await RecordAsync(counter, customerId, purpose, "Granted", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(response)).ShouldBe("customers.value-required");
    }

    /// <summary>
    /// One route and two trail actions. A reviewer looking for withdrawals searches for one name, and
    /// the entry carries the purpose, the outcome and the wording version and nothing else about her.
    /// </summary>
    [Fact]
    public async Task AWithdrawalIsWrittenToTheTrailUnderItsOwnAction()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-audit", "203.0.113.147");
        var customerId = await CustomerAsync();
        var purpose = await CustomerHarness.PurposeAsync(fixture);

        await RecordAsync(counter, customerId, purpose, "Granted", "counter, verbal");
        await RecordAsync(counter, customerId, purpose, "Withdrawn", "telephone");

        var entries = await AuditEntriesAsync(customerId);

        entries.Select(entry => entry.Action).ShouldBe(
            ["customers.consent.recorded", "customers.consent.withdrawn"], ignoreOrder: true);

        var withdrawal = entries.Single(entry => entry.Action == "customers.consent.withdrawn");

        withdrawal.Summary.ShouldContain(purpose);
        withdrawal.Summary.ShouldContain("Withdrawn");
        withdrawal.Before.ShouldNotBeNull().ShouldContain("Granted");
        withdrawal.After.ShouldNotBeNull().ShouldContain("Withdrawn");

        // The free text somebody typed to say where the answer came from is not what section 5.3
        // approves for the trail, and it is the one field an operator could put anything in.
        string.Join('\n', entries.Select(entry => $"{entry.Summary}{entry.Before}{entry.After}"))
            .ShouldNotContain("telephone");
    }

    [Fact]
    public async Task ReadingConsentNeedsThePermissionThatOwnsIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();

        using var withoutIt = await CustomerHarness.CounterAsync(
            fixture, "consent-forbidden", "203.0.113.148", BranchId, CustomersPermissions.Read);

        (await withoutIt.GetAsync($"/api/v1/customers/{customerId}/consent"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await withoutIt.GetAsync($"/api/v1/customers/{customerId}/communication-preferences"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The register comes back whole, including the five purposes <c>init-reference-data</c> seeds and
    /// has deliberately published no wording for. A purpose nobody has asked about is the prompt to
    /// ask, which is why it is in the answer rather than absent from it.
    /// </summary>
    [Fact]
    public async Task TheRegisterComesBackWholeIncludingWhatNobodyHasBeenAskedAbout()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        await CustomerHarness.SeededPurposesAsync(fixture);

        using var counter = await CounterAsync("consent-register", "203.0.113.149");
        var customerId = await CustomerAsync();

        var consent = await ReadConsentAsync(counter, customerId);

        foreach (var key in new[]
        {
            "measurement_storage", "photo_capture", "transactional_messages", "marketing_messages",
            "feedback_requests",
        })
        {
            var state = consent.Purposes.SingleOrDefault(entry => entry.Key == key)
                .ShouldNotBeNull($"the register does not carry the seeded purpose '{key}'.");

            state.Status.ShouldBe("NeverAsked");
            state.Answers.ShouldBeEmpty();
        }
    }

    /* Communication preferences ------------------------------------------------------------------ */

    [Fact]
    public async Task ACustomerNobodyHasAskedHasNoPreferenceAndNoVersionToChangeItAgainst()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("pref-unrecorded", "203.0.113.150");
        var customerId = await CustomerAsync();

        var response = await counter.GetAsync(
            $"/api/v1/customers/{customerId}/communication-preferences");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldBeNull();

        var preference = await ReadAsync<PreferenceBody>(response);
        preference.HasBeenRecorded.ShouldBeFalse();
        preference.AllowedChannels.ShouldBeEmpty();
        preference.Language.ShouldBe("ta-IN");
        preference.Version.ShouldBeNull();
    }

    /// <summary>
    /// The first write is the one change that needs no precondition, and every one after it needs one.
    /// Requiring <c>If-Match</c> on the first would make the preference unrecordable; not requiring it
    /// on the second would let two counters silently overwrite each other.
    /// </summary>
    [Fact]
    public async Task TheFirstPreferenceNeedsNoPreconditionAndTheNextOneDoes()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("pref-precondition", "203.0.113.151");
        var customerId = await CustomerAsync();

        var first = await ReplaceAsync(counter, customerId, ["Sms", "WhatsApp"], "en-IN", "21:30", "08:00");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.ETag.ShouldNotBeNull();

        var recorded = await ReadAsync<PreferenceBody>(first);
        recorded.HasBeenRecorded.ShouldBeTrue();
        recorded.AllowedChannels.ShouldBe(["Sms", "WhatsApp"]);
        recorded.Language.ShouldBe("en-IN");
        recorded.QuietHoursStart.ShouldBe("21:30:00");
        recorded.QuietHoursEnd.ShouldBe("08:00:00");
        recorded.Version.ShouldNotBeNullOrWhiteSpace();

        (await ReplaceAsync(counter, customerId, ["Email"], null, null, null))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        var second = await ReplaceAsync(
            counter, customerId, ["Email"], null, null, null, recorded.Version);

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync<PreferenceBody>(second)).AllowedChannels.ShouldBe(["Email"]);
    }

    [Fact]
    public async Task AStalePreconditionIsRefusedRatherThanOverwriting()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("pref-stale", "203.0.113.152");
        var customerId = await CustomerAsync();

        var stale = (await ReadAsync<PreferenceBody>(
            await ReplaceAsync(counter, customerId, ["Sms"], null, null, null))).Version;

        (await ReplaceAsync(counter, customerId, ["Email"], null, null, null, stale))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await ReplaceAsync(counter, customerId, ["WhatsApp"], null, null, null, stale);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthenticationClient.CodeAsync(response)).ShouldBe("customers.concurrent-change");

        // The losing write changed nothing.
        (await PreferenceAsync(customerId)).AllowedChannels.ShouldBe([MessageChannel.Email]);
    }

    /// <summary>
    /// "Do not message me" is an answer. It leaves nothing to send on and withdraws consent to nothing,
    /// and it is not the same as never having been asked.
    /// </summary>
    [Fact]
    public async Task AllowingNoChannelIsRecordedAsAChoice()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("pref-none", "203.0.113.153");
        var customerId = await CustomerAsync();

        var response = await ReplaceAsync(counter, customerId, [], null, null, null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var preference = await ReadAsync<PreferenceBody>(response);
        preference.HasBeenRecorded.ShouldBeTrue();
        preference.AllowedChannels.ShouldBeEmpty();

        var published = await PreferenceAsync(customerId);
        published.HasBeenRecorded.ShouldBeTrue();
        published.Allows(MessageChannel.Sms).ShouldBeFalse();
    }

    [Fact]
    public async Task AChannelTheShopCannotSendOnIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("pref-channel", "203.0.113.154");
        var customerId = await CustomerAsync();

        var response = await ReplaceAsync(counter, customerId, ["Pigeon"], null, null, null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(response))
            .ShouldBe("customers.communication-channel-not-understood");
    }

    [Fact]
    public async Task HalfAQuietWindowIsRefused()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("pref-quiet", "203.0.113.155");
        var customerId = await CustomerAsync();

        var response = await ReplaceAsync(counter, customerId, ["Sms"], null, "21:30", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthenticationClient.CodeAsync(response)).ShouldBe("customers.quiet-hours-incomplete");
    }

    [Fact]
    public async Task ChangingThePreferenceIsWrittenToTheTrailAsAStateAndNotAsAWindow()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("pref-audit", "203.0.113.156");
        var customerId = await CustomerAsync();

        await ReplaceAsync(counter, customerId, ["Sms"], "ta-IN", "22:15", "07:45");

        var entry = (await AuditEntriesAsync(customerId))
            .Single(row => row.Action == "customers.preferences.changed");

        entry.After.ShouldNotBeNull().ShouldContain("Sms");
        entry.After.ShouldContain("ta-IN");

        // The shape, not the window. A reader of the trail needs to know she asked not to be messaged
        // at night; the hours are on the record for whoever may read it.
        entry.After.ShouldNotContain("22:15");
        entry.After.ShouldContain("hasQuietHours");
    }

    /* The events -------------------------------------------------------------------------------- */

    /// <summary>
    /// Recording an answer writes its event into <strong>this module's</strong> outbox, in the same
    /// save.
    /// </summary>
    /// <remarks>
    /// The assertion that matters is the schema. Before #77 there was one shared
    /// <c>platform.outbox_messages</c> on another context, so the event and the consent record were
    /// two transactions and a crash between them lost one of them. Reading the row out of
    /// <c>customers.outbox_messages</c> is what says that is no longer true.
    /// </remarks>
    [Fact]
    public async Task RecordingAnAnswerPublishesItsEventIntoTheCustomersOutbox()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-event", "203.0.113.170");
        var customerId = await CustomerAsync();
        var purpose = await CustomerHarness.PurposeAsync(fixture);

        var response = await RecordAsync(counter, customerId, purpose, "Granted", "counter, verbal");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var answer = await ReadAsync<AnswerBody>(response);
        var message = (await OutboxAsync(customerId))
            .SingleOrDefault(row => row.EventType == "customers.consent-recorded.v1")
            .ShouldNotBeNull("The answer committed without its event.");

        message.SchemaVersion.ShouldBe(1);
        message.AggregateId.ShouldBe(customerId);

        var payload = JsonDocument.Parse(message.Payload).RootElement;
        payload.GetProperty("recordId").GetGuid().ShouldBe(answer.RecordId);
        payload.GetProperty("purposeKey").GetString().ShouldBe(purpose);
        payload.GetProperty("wordingVersion").GetInt32().ShouldBe(1);
        payload.GetProperty("branchId").GetGuid().ShouldBe(BranchId);

        // The name, not the ordinal. An operator reading a dead-lettered row sees what she said.
        payload.GetProperty("status").GetString().ShouldBe("Granted");

        // conventions.md section 5.5 admits identifiers, codes, statuses, timestamps, amounts and
        // branch codes. "counter, verbal" is free text: it is on the record, and IConsentQuery hands
        // it to a consumer approved to read it.
        payload.TryGetProperty("source", out _).ShouldBeFalse(
            "The payload carries the free-text source, which conventions.md section 5.5 does not "
            + "admit and which fans out to every registered handler.");
    }

    /// <summary>
    /// A withdrawal publishes its own event, so a consumer that must honour one subscribes to exactly
    /// that fact rather than to every answer with a filter it can forget.
    /// </summary>
    [Fact]
    public async Task WithdrawingPublishesItsOwnEventAndNotARecordedOne()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-wd-event", "203.0.113.171");
        var customerId = await CustomerAsync();
        var purpose = await CustomerHarness.PurposeAsync(fixture);

        await RecordAsync(counter, customerId, purpose, "Granted", "counter, verbal");
        await RecordAsync(counter, customerId, purpose, "Withdrawn", "telephone");

        var messages = await OutboxAsync(customerId);

        messages.Select(row => row.EventType).ShouldBe(
            ["customers.consent-recorded.v1", "customers.consent-withdrawn.v1"],
            "The grant and the withdrawal are two events, in the order she gave them.");

        messages.Select(row => row.SchemaVersion).ShouldAllBe(version => version == 1);

        var withdrawal = JsonDocument
            .Parse(messages[1].Payload)
            .RootElement;

        // The event type is the status, so there is no status field a consumer has to check.
        withdrawal.TryGetProperty("status", out _).ShouldBeFalse();
        withdrawal.GetProperty("purposeKey").GetString().ShouldBe(purpose);

        // Both are the customer's, which is what makes the dispatcher rank them against each other at
        // all — it claims only the oldest undelivered message of an aggregate. That ordering holds
        // over committed rows and does not serialise two counters answering at once; the limit is in
        // docs/integration/events/README.md section 4.1, and these two answers are sequential.
        messages.Select(row => row.AggregateId).Distinct().ShouldHaveSingleItem().ShouldBe(customerId);
    }

    /// <summary>
    /// A refused answer publishes nothing, because the event is committed by the same save as the
    /// record and there was no record.
    /// </summary>
    [Fact]
    public async Task ARefusedAnswerLeavesNoEventBehind()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("consent-no-event", "203.0.113.172");
        var customerId = await CustomerAsync();
        var retired = await CustomerHarness.PurposeAsync(fixture, retired: true);

        var response = await RecordAsync(counter, customerId, retired, "Granted", "counter, verbal");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await OutboxAsync(customerId)).ShouldBeEmpty(
            "An answer the module refused announced itself anyway.");
    }

    /// <summary>
    /// Replacing a preference publishes a change notification carrying no preference.
    /// </summary>
    /// <remarks>
    /// The negative half is the point. Channels, language and quiet hours are Personal under
    /// <c>docs/nfr/data-classification.md</c> section 5.3, whose consumer "reads it through
    /// <c>IConsentQuery</c> and never copies it" — and an outbox row is a copy that fans out to every
    /// registered handler and outlives the moment. The event says the answer changed; the reader asks
    /// what it now is.
    /// </remarks>
    [Fact]
    public async Task ChangingThePreferencePublishesThatItChangedAndNothingAboutIt()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        using var counter = await CounterAsync("pref-event", "203.0.113.173");
        var customerId = await CustomerAsync();

        await ReplaceAsync(counter, customerId, ["Sms"], "ta-IN", "22:15", "07:45");

        var first = (await OutboxAsync(customerId)).ShouldHaveSingleItem();
        first.EventType.ShouldBe("customers.preferences-changed.v1");
        first.AggregateId.ShouldBe(customerId);

        var payload = JsonDocument.Parse(first.Payload).RootElement;
        payload.GetProperty("wasFirstRecorded").GetBoolean().ShouldBeTrue();

        foreach (var withheld in new[]
        {
            "allowedChannels", "language", "quietHours", "quietHoursStart", "quietHoursEnd",
        })
        {
            payload.TryGetProperty(withheld, out _).ShouldBeFalse(
                $"The payload carries '{withheld}'. Section 5.3 classifies it Personal and names who "
                + "may access it; an outbox row broadcasts it to every registered handler.");
        }
    }

    /* Arrangement -------------------------------------------------------------------------------- */

    private async Task<Guid> CustomerAsync()
    {
        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);

        return await CustomerHarness.CustomerAsync(fixture, BranchId, "ta-IN");
    }

    /// <summary>
    /// A signed-in counter at this class's branch.
    /// </summary>
    /// <remarks>
    /// The branch is opened first, and that ordering is not incidental: an account is assigned to a
    /// branch by a row with a foreign key to it, so creating the account before the branch fails on
    /// the constraint. It passed for a while against a database that already carried the branch from
    /// an earlier run, which is what a fresh one is for.
    /// </remarks>
    private async Task<AuthenticationClient> CounterAsync(string prefix, string address)
    {
        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);

        return await CustomerHarness.CounterAsync(
            fixture, prefix, address, BranchId, CustomerHarness.Reception);
    }

    private static Task<HttpResponseMessage> RecordAsync(
        AuthenticationClient client,
        Guid customerId,
        string purposeKey,
        string decision,
        string? source)
        => client.PostAsync(
            $"/api/v1/customers/{customerId}/consent",
            new { purposeKey, decision, source },
            Key());

    private static Task<HttpResponseMessage> ReplaceAsync(
        AuthenticationClient client,
        Guid customerId,
        string[] allowedChannels,
        string? language,
        string? quietHoursStart,
        string? quietHoursEnd,
        string? version = null)
        => client.PutAsync(
            $"/api/v1/customers/{customerId}/communication-preferences",
            new { allowedChannels, language, quietHoursStart, quietHoursEnd },
            version is null ? [Key()] : [Key(), ("If-Match", $"\"{version}\"")]);

    private static async Task<ConsentBody> ReadConsentAsync(
        AuthenticationClient client,
        Guid customerId)
    {
        var response = await client.GetAsync($"/api/v1/customers/{customerId}/consent");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();

        return await ReadAsync<ConsentBody>(response);
    }

    private static async Task<TBody> ReadAsync<TBody>(HttpResponseMessage response)
        where TBody : class
        => (await AuthenticationClient.ReadAsync<TBody>(response)).ShouldNotBeNull();

    private async Task<ConsentState> ConsentAsync(Guid customerId, string purposeKey)
    {
        using var scope = fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IConsentQuery>()
            .GetAsync(customerId, purposeKey, TestContext.Current.CancellationToken);
    }

    private async Task<CommunicationPreference> PreferenceAsync(Guid customerId)
    {
        using var scope = fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ICommunicationPreferenceQuery>()
            .GetAsync(customerId, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// This customer's outbox rows, from the Customers schema, oldest first.
    /// </summary>
    /// <remarks>
    /// Read through <c>CustomersDbContext</c>, which maps <c>customers.outbox_messages</c> and no
    /// other module's. A test that read the platform's table would pass against the arrangement #77
    /// replaced and say nothing about this one.
    /// </remarks>
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

    private sealed record AuditRow(string Action, string Summary, string? Before, string? After);

    private sealed record OutboxRow(
        string EventType,
        int SchemaVersion,
        Guid AggregateId,
        string Payload);

    private sealed record ConsentBody(IReadOnlyList<PurposeBody> Purposes);

    private sealed record PurposeBody(
        string Key,
        string Name,
        string? Description,
        bool IsRetired,
        int CurrentWordingVersion,
        bool CanBeAnswered,
        string Status,
        IReadOnlyList<AnswerBody> Answers);

    private sealed record AnswerBody(
        Guid RecordId,
        string PurposeKey,
        string Decision,
        int WordingVersion,
        DateTimeOffset RecordedAt,
        string Source,
        Guid? RecordedBy,
        Guid? BranchId);

    private sealed record PreferenceBody(
        Guid CustomerId,
        bool HasBeenRecorded,
        IReadOnlyList<string> AllowedChannels,
        string Language,
        string? QuietHoursStart,
        string? QuietHoursEnd,
        DateTimeOffset? UpdatedAt,
        string? Version);
}
