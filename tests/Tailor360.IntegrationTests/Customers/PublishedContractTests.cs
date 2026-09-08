using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Customers.Application.Consent;
using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Modules.Customers.Contracts.Preferences;
using Tailor360.Modules.Customers.Domain.Consent;
using Tailor360.Modules.Customers.Domain.Preferences;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.IntegrationTests.Customers;

/// <summary>
/// The three queries this module publishes, asked of a real database.
/// </summary>
/// <remarks>
/// <para>
/// These are the module's boundary, and every one of them is answered by a query rather than by code
/// a substitute could stand in for: the standing consent answer is an ordering over an append-only
/// table, the preference language falls back to a column on another table, and the contact mask is a
/// projection PostgreSQL evaluates. Asked of a fake, all three would prove only that the fake agreed
/// with itself.
/// </para>
/// <para>
/// They are resolved from the composed application rather than constructed, because half of what is
/// under test is that <c>AddCustomersModule</c> registers them at all — a consumer that cannot resolve
/// <see cref="IConsentQuery"/> discovers it on the first send, in production, at the moment it is
/// deciding whether it may message somebody.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class PublishedContractTests(WebApplicationFixture fixture)
{
    private static readonly Guid BranchId = Guid.Parse("0199c000-0000-7000-8000-0000000000c5");

    private const string BranchCode = "CUST5";

    /// <summary>
    /// Nine in the morning at a Coimbatore counter, written as the instant it is.
    /// </summary>
    /// <remarks>
    /// Every instant this system stores is UTC — the module context maps them to <c>timestamptz</c>
    /// and Npgsql refuses any other offset outright — so a fixture writing an answer "given at nine"
    /// writes 03:30Z rather than 09:00+05:30. Business <em>dates</em> are the ones evaluated in the
    /// branch's timezone; the moment somebody said something is not one of them.
    /// </remarks>
    private static readonly DateTimeOffset Morning =
        new(2026, 5, 7, 3, 30, 0, TimeSpan.Zero);

    /* Consent ----------------------------------------------------------------------------------- */

    [Fact]
    public async Task ACustomerNobodyHasAskedGrantsNothingRatherThanAnsweringNull()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();

        var state = await ConsentAsync(customerId, ConsentPurposeKeys.PhotoCapture);

        state.Status.ShouldBe(ConsentStatus.NeverAsked);
        state.IsGranted.ShouldBeFalse();
        state.RecordId.ShouldBeNull();
        state.WordingVersion.ShouldBeNull();
        state.RecordedAt.ShouldBeNull();
        state.Source.ShouldBeNull();
    }

    /// <summary>
    /// A customer identifier that matches nothing is answered, not refused. The query says what may be
    /// done and the answer is nothing; whether the customer exists is a different question, and
    /// <see cref="ICustomerSnapshotQuery"/> is the one that answers it.
    /// </summary>
    [Fact]
    public async Task ACustomerWhoDoesNotExistGrantsNothingRatherThanFailing()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var state = await ConsentAsync(Guid.CreateVersion7(), ConsentPurposeKeys.PhotoCapture);

        state.Status.ShouldBe(ConsentStatus.NeverAsked);
        state.IsGranted.ShouldBeFalse();
    }

    /// <summary>
    /// A purpose key that is blank or nothing but spaces names no purpose, so it grants nothing. It is
    /// answered rather than refused for the same reason a customer nobody has heard of is: a consumer
    /// deciding whether it may send has to be told no, not handed an exception to catch.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task APurposeKeyNamingNothingGrantsNothing(string purposeKey)
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();

        await CustomerHarness.ConsentAsync(
            fixture, customerId, ConsentPurposeKeys.PhotoCapture, ConsentDecision.Granted, Morning);

        var state = await ConsentAsync(customerId, purposeKey);

        state.Status.ShouldBe(ConsentStatus.NeverAsked);
        state.IsGranted.ShouldBeFalse();
    }

    /// <summary>
    /// The whole reason the answer is computed rather than stored. She agreed, she withdrew, she
    /// agreed again — three rows, none of them edited, and the standing answer is the last of them.
    /// </summary>
    [Fact]
    public async Task TheStandingAnswerIsTheLatestRecordAndTheEarlierOnesStillStand()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();
        var purpose = ConsentPurposeKeys.MarketingMessages;

        await CustomerHarness.ConsentAsync(
            fixture, customerId, purpose, ConsentDecision.Granted, Morning, source: "counter, verbal");

        (await ConsentAsync(customerId, purpose)).Status.ShouldBe(ConsentStatus.Granted);

        await CustomerHarness.ConsentAsync(
            fixture, customerId, purpose, ConsentDecision.Withdrawn, Morning.AddDays(30),
            source: "telephone");

        (await ConsentAsync(customerId, purpose)).Status.ShouldBe(ConsentStatus.Withdrawn);

        var latest = await CustomerHarness.ConsentAsync(
            fixture, customerId, purpose, ConsentDecision.Granted, Morning.AddDays(90),
            wordingVersion: 2, source: "counter, written");

        var state = await ConsentAsync(customerId, purpose);

        state.Status.ShouldBe(ConsentStatus.Granted);
        state.IsGranted.ShouldBeTrue();
        state.RecordId.ShouldBe(latest);
        state.WordingVersion.ShouldBe(2);
        state.RecordedAt.ShouldBe(Morning.AddDays(90));
        state.Source.ShouldBe("counter, written");
    }

    /// <summary>
    /// Two answers sharing an instant have to resolve to one standing answer, and to the same one on
    /// every read.
    /// </summary>
    /// <remarks>
    /// The tie-break is the identifier, and it is a tie-break rather than a judgement about which was
    /// given second — a version-7 identifier orders by time only to the millisecond, and two generated
    /// inside one differ only in random bits. What the query owes a caller is a <em>total, repeatable</em>
    /// answer, so this asks for exactly that: read it several times and get the same row, and get the
    /// row PostgreSQL's own ordering picks rather than one .NET's differently-ordered <c>Guid</c>
    /// comparison would.
    /// </remarks>
    [Fact]
    public async Task TwoAnswersAtTheSameInstantResolveToOneAnswerAndAlwaysTheSameOne()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();
        var purpose = ConsentPurposeKeys.FeedbackRequests;

        var first = await CustomerHarness.ConsentAsync(
            fixture, customerId, purpose, ConsentDecision.Declined, Morning);
        var second = await CustomerHarness.ConsentAsync(
            fixture, customerId, purpose, ConsentDecision.Granted, Morning);

        var state = await ConsentAsync(customerId, purpose);

        // One of the two, never neither and never something else.
        state.RecordId.ShouldBeOneOf(first, second);

        // And the same one every time. A tie broken by whatever order the database returned would
        // make a customer's consent depend on the query plan.
        for (var read = 0; read < 5; read++)
        {
            var again = await ConsentAsync(customerId, purpose);

            again.RecordId.ShouldBe(state.RecordId);
            again.Status.ShouldBe(state.Status);
        }

        // The screen and the contract read the same rows and must not order them differently, which
        // is the failure a second sort in memory would introduce.
        var handled = await StandingAnswerAsync(customerId, purpose);
        handled.ShouldBe(state.RecordId);
    }

    /// <summary>
    /// Consent is per purpose. Agreeing to have her measurements kept says nothing about whether the
    /// shop may photograph the garment, and a query that let one answer the other would be the single
    /// most damaging way this contract could be wrong.
    /// </summary>
    [Fact]
    public async Task OneAgreedPurposeSaysNothingAboutAnother()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();

        await CustomerHarness.ConsentAsync(
            fixture, customerId, ConsentPurposeKeys.MeasurementStorage, ConsentDecision.Granted, Morning);

        (await ConsentAsync(customerId, ConsentPurposeKeys.MeasurementStorage))
            .IsGranted.ShouldBeTrue();
        (await ConsentAsync(customerId, ConsentPurposeKeys.PhotoCapture))
            .Status.ShouldBe(ConsentStatus.NeverAsked);
    }

    /// <summary>
    /// Consent is per customer for the same reason it is per purpose. One customer's answer must never
    /// stand in for another's.
    /// </summary>
    [Fact]
    public async Task OneCustomersAnswerIsNotAnother()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var agreed = await CustomerAsync();
        var neverAsked = await CustomerAsync();

        await CustomerHarness.ConsentAsync(
            fixture, agreed, ConsentPurposeKeys.PhotoCapture, ConsentDecision.Granted, Morning);

        (await ConsentAsync(agreed, ConsentPurposeKeys.PhotoCapture)).IsGranted.ShouldBeTrue();
        (await ConsentAsync(neverAsked, ConsentPurposeKeys.PhotoCapture)).IsGranted.ShouldBeFalse();
    }

    /* Preferences ------------------------------------------------------------------------------- */

    /// <summary>
    /// Nobody has asked her which channels she accepts, but she did say at the counter which language
    /// to write to her in. The query answers with that rather than with a default it invented.
    /// </summary>
    [Fact]
    public async Task AnUnrecordedPreferenceFallsBackToTheLanguageOnTheCustomersOwnRecord()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync("ta-IN");

        var preference = await PreferenceAsync(customerId);

        preference.HasBeenRecorded.ShouldBeFalse();
        preference.AllowedChannels.ShouldBeEmpty();
        preference.Language.ShouldBe("ta-IN");
        preference.QuietHours.ShouldBeNull();
        preference.Allows(MessageChannel.Sms).ShouldBeFalse();
    }

    [Fact]
    public async Task ACustomerWhoDoesNotExistIsUnreachableRatherThanFailing()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var preference = await PreferenceAsync(Guid.CreateVersion7());

        preference.HasBeenRecorded.ShouldBeFalse();
        preference.AllowedChannels.ShouldBeEmpty();
        preference.Language.ShouldBe("en-IN");
    }

    [Fact]
    public async Task ARecordedPreferenceCarriesItsChannelsLanguageAndQuietHours()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync("ta-IN");
        var quiet = QuietHours.TryRead(new TimeOnly(21, 30), new TimeOnly(8, 0)).Value.ShouldNotBeNull();

        await CustomerHarness.PreferenceAsync(
            fixture,
            customerId,
            [CommunicationChannel.Email, CommunicationChannel.Sms],
            "en-IN",
            quiet);

        var preference = await PreferenceAsync(customerId);

        preference.HasBeenRecorded.ShouldBeTrue();
        preference.Language.ShouldBe("en-IN");

        // Ordered by the enumeration rather than by the order they were written, so that two requests
        // saying the same thing produce the same answer.
        preference.AllowedChannels.ShouldBe([MessageChannel.Sms, MessageChannel.Email]);
        preference.Allows(MessageChannel.WhatsApp).ShouldBeFalse();

        preference.QuietHours.ShouldNotBeNull();
        preference.QuietHours.Start.ShouldBe(new TimeOnly(21, 30));
        preference.QuietHours.End.ShouldBe(new TimeOnly(8, 0));
        preference.QuietHours.Covers(new TimeOnly(23, 0)).ShouldBeTrue();
        preference.QuietHours.Covers(new TimeOnly(9, 0)).ShouldBeFalse();
    }

    /// <summary>
    /// "Do not message me" is an answer, and it is not the same as never having been asked. Both leave
    /// the channel set empty, and only the second is a reason to ask her.
    /// </summary>
    [Fact]
    public async Task ACustomerWhoChoseNoChannelIsRecordedAsHavingChosen()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();

        await CustomerHarness.PreferenceAsync(fixture, customerId, []);

        var preference = await PreferenceAsync(customerId);

        preference.HasBeenRecorded.ShouldBeTrue();
        preference.AllowedChannels.ShouldBeEmpty();
        preference.Allows(MessageChannel.Sms).ShouldBeFalse();
    }

    /* Snapshot ---------------------------------------------------------------------------------- */

    [Fact]
    public async Task ACallerHoldingTheContactPermissionSeesTheContactFields()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync("ta-IN");

        var snapshot = (await SnapshotAsync(
            customerId, CustomersPermissions.Read, CustomersPermissions.ReadContact)).ShouldNotBeNull();

        snapshot.CustomerId.ShouldBe(customerId);
        snapshot.CustomerNumber.ShouldStartWith("C-CONTRACT-");
        snapshot.DisplayName.ShouldStartWith("Contract subject ");
        snapshot.NativeName.ShouldNotBeNullOrWhiteSpace();
        snapshot.Language.ShouldBe("ta-IN");
        snapshot.OwningBranchId.ShouldBe(BranchId);

        snapshot.ContactIncluded.ShouldBeTrue();

        // A well-formed Indian number, rather than the prefix the harness's generator happens to use.
        // Pinning the prefix made this test an assertion about test data: it broke the day the
        // generator was widened to stop colliding, and it had never been checking anything the
        // contract promises.
        snapshot.PhoneE164.ShouldNotBeNull();
        snapshot.PhoneE164.ShouldStartWith("+91");
        snapshot.PhoneE164!.Length.ShouldBe(13);
        snapshot.PhoneE164[3..].ShouldAllBe(digit => char.IsAsciiDigit(digit));
        snapshot.Email.ShouldNotBeNullOrWhiteSpace();
        snapshot.AddressLine.ShouldBe("12 Trichy Road");
        snapshot.Locality.ShouldBe("Ramanathapuram");
        snapshot.Postcode.ShouldBe("641045");
    }

    /// <summary>
    /// The field-level minimisation of <c>data-classification.md</c> section 5.2, asked at the module
    /// boundary rather than at the wire: the same record, read by a caller who holds
    /// <c>customers.read</c> and not <c>customers.read_contact</c>.
    /// </summary>
    [Fact]
    public async Task ACallerWithoutTheContactPermissionSeesNoneOfThemAndIsToldSo()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var customerId = await CustomerAsync();

        var snapshot = (await SnapshotAsync(customerId, CustomersPermissions.Read)).ShouldNotBeNull();

        snapshot.DisplayName.ShouldStartWith("Contract subject ");
        snapshot.CustomerNumber.ShouldStartWith("C-CONTRACT-");

        // Told so, rather than left to guess. A blank address because the caller was masked is a
        // defect; a blank address because there is none is a fact, and an invoice has to tell them
        // apart.
        snapshot.ContactIncluded.ShouldBeFalse();
        snapshot.PhoneE164.ShouldBeNull();
        snapshot.AlternatePhoneE164.ShouldBeNull();
        snapshot.Email.ShouldBeNull();
        snapshot.AddressLine.ShouldBeNull();
        snapshot.Locality.ShouldBeNull();
        snapshot.Postcode.ShouldBeNull();
    }

    [Fact]
    public async Task ACustomerWhoDoesNotExistHasNoSnapshot()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        (await SnapshotAsync(Guid.CreateVersion7(), CustomersPermissions.ReadContact)).ShouldBeNull();
    }

    /* Arrangement ------------------------------------------------------------------------------- */

    private async Task<Guid> CustomerAsync(string language = "en-IN")
    {
        await CustomerHarness.BranchAsync(fixture, BranchId, BranchCode);

        return await CustomerHarness.CustomerAsync(fixture, BranchId, language);
    }

    /// <summary>What the counter screen would show as the standing answer for one purpose.</summary>
    private async Task<Guid?> StandingAnswerAsync(Guid customerId, string purposeKey)
    {
        using var scope = fixture.Services.CreateScope();

        var consent = await scope.ServiceProvider.GetRequiredService<ConsentHandler>()
            .ReadAsync(customerId, SessionTestData.OrganisationId, TestContext.Current.CancellationToken);

        consent.IsSuccess.ShouldBeTrue();

        var answers = consent.Value.Purposes
            .Single(purpose => purpose.Key == purposeKey)
            .Answers;

        return answers.Count == 0 ? null : answers[0].RecordId;
    }

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

    private async Task<CustomerSnapshot?> SnapshotAsync(Guid customerId, params string[] permissions)
    {
        using var scope = fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ICustomerSnapshotQuery>()
            .GetAsync(customerId, permissions, TestContext.Current.CancellationToken);
    }
}
