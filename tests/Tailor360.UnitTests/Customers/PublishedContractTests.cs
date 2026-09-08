using Shouldly;
using Tailor360.Modules.Customers.Contracts.Consent;
using Tailor360.Modules.Customers.Contracts.Customers;
using Tailor360.Modules.Customers.Contracts.Preferences;
using Tailor360.Modules.Customers.Domain.Preferences;
using Tailor360.Platform.Security.Permissions;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The module's published surface: the answers a consumer gets, and the three places where the
/// architecture forces something to be written twice.
/// </summary>
/// <remarks>
/// A <c>Contracts</c> project may reference <c>Platform.Abstractions</c> and nothing else, so it can
/// share neither the domain's enumerations nor the security catalogue's permission keys. Three things
/// are therefore duplicates by design — the channel set, the quiet-hours arithmetic and the
/// contact-permission key — and each of them is a defect waiting for the day the two copies stop
/// agreeing. The tests below are what stops that day arriving unnoticed.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PublishedContractTests
{
    private static readonly Guid Customer = Guid.Parse("0199c000-0000-7000-8000-00000000c001");

    /* Consent ----------------------------------------------------------------------------------- */

    /// <summary>
    /// The reason the published enumeration has a member the domain's does not: a consumer asking
    /// about a customer nobody has asked must get an answer, and the answer must permit nothing.
    /// </summary>
    [Fact]
    public void ACustomerNobodyHasAskedHasAnAnswerAndItGrantsNothing()
    {
        var state = ConsentState.NeverAsked(Customer, "photo_capture");

        state.CustomerId.ShouldBe(Customer);
        state.PurposeKey.ShouldBe("photo_capture");
        state.Status.ShouldBe(ConsentStatus.NeverAsked);
        state.IsGranted.ShouldBeFalse();
        state.WordingVersion.ShouldBeNull();
        state.RecordedAt.ShouldBeNull();
        state.Source.ShouldBeNull();
        state.RecordId.ShouldBeNull();
    }

    /// <summary>
    /// A status that arrives default-constructed, or deserialised from a payload that omitted the
    /// field, must be the one that permits nothing. That is only true if it is the zero value, and
    /// nothing but a test says so.
    /// </summary>
    [Fact]
    public void TheStatusThatPermitsNothingIsTheZeroValue()
    {
        default(ConsentStatus).ShouldBe(ConsentStatus.NeverAsked);
    }

    [Theory]
    [InlineData(ConsentStatus.Granted, true)]
    [InlineData(ConsentStatus.Declined, false)]
    [InlineData(ConsentStatus.Withdrawn, false)]
    [InlineData(ConsentStatus.NeverAsked, false)]
    public void OnlyAGrantedAnswerGrantsAnything(ConsentStatus status, bool granted)
    {
        var state = new ConsentState(Customer, "marketing_messages", status, 1, null, null, null);

        state.IsGranted.ShouldBe(granted);
    }

    /* Preferences ------------------------------------------------------------------------------- */

    /// <summary>
    /// The distinction the type exists to carry: a customer who chose no channel and a customer nobody
    /// has asked are both unreachable, and only the second is a reason to ask her.
    /// </summary>
    [Fact]
    public void ACustomerWhosePreferenceNobodyRecordedIsUnreachableAndSaysSo()
    {
        var preference = CommunicationPreference.NotRecorded(Customer, "ta-IN");

        preference.HasBeenRecorded.ShouldBeFalse();
        preference.AllowedChannels.ShouldBeEmpty();
        preference.Language.ShouldBe("ta-IN");
        preference.QuietHours.ShouldBeNull();
        preference.Allows(MessageChannel.Sms).ShouldBeFalse();
        preference.Allows(MessageChannel.WhatsApp).ShouldBeFalse();
        preference.Allows(MessageChannel.Email).ShouldBeFalse();
    }

    [Fact]
    public void ARecordedPreferenceAllowsOnlyTheChannelsItNames()
    {
        var preference = new CommunicationPreference(
            Customer, HasBeenRecorded: true, [MessageChannel.Sms, MessageChannel.Email], "en-IN", null);

        preference.Allows(MessageChannel.Sms).ShouldBeTrue();
        preference.Allows(MessageChannel.Email).ShouldBeTrue();
        preference.Allows(MessageChannel.WhatsApp).ShouldBeFalse();
    }

    /// <summary>
    /// The published channel set and the domain's are separate types that must name the same things.
    /// Adding a channel to one and not the other would make it unreachable through the contract, which
    /// is a message that silently never goes out rather than an error anybody sees.
    /// </summary>
    [Fact]
    public void ThePublishedChannelsAreTheChannelsTheModuleKnows()
    {
        Enum.GetNames<MessageChannel>().ShouldBe(Enum.GetNames<CommunicationChannel>());
    }

    /* Quiet hours ------------------------------------------------------------------------------- */

    /// <summary>
    /// The midnight-wrapping arithmetic is written twice because the architecture forbids the shared
    /// type that would let it be written once. So it is asserted at every minute of the day, against
    /// windows that run forwards, backwards and right up to the boundary.
    /// </summary>
    [Theory]
    [InlineData(21, 30, 8, 0)]    // The ordinary case: quiet overnight.
    [InlineData(13, 0, 15, 30)]   // A window inside one day.
    [InlineData(0, 0, 23, 59)]    // Almost the whole day, forwards.
    [InlineData(23, 59, 0, 1)]    // Two minutes across midnight.
    public void TheTwoCopiesOfTheQuietWindowAgreeAtEveryMinuteOfTheDay(
        int startHour, int startMinute, int endHour, int endMinute)
    {
        var start = new TimeOnly(startHour, startMinute);
        var end = new TimeOnly(endHour, endMinute);

        var published = new QuietWindow(start, end);
        var domain = QuietHours.TryRead(start, end).Value.ShouldNotBeNull();

        for (var minute = 0; minute < 24 * 60; minute++)
        {
            var local = new TimeOnly(minute / 60, minute % 60);

            published.Covers(local).ShouldBe(
                domain.Covers(local),
                $"the two copies disagree at {local:HH\\:mm} for {start:HH\\:mm}–{end:HH\\:mm}");
        }
    }

    /* Snapshot ---------------------------------------------------------------------------------- */

    /// <summary>
    /// The contract repeats a permission key the security catalogue owns, because it may not reference
    /// the project that declares it. A copy that drifted would match nothing, and matching nothing
    /// masks the contact fields from everybody — a failure that looks like working software until an
    /// invoice prints without an address.
    /// </summary>
    [Fact]
    public void TheRepeatedContactPermissionIsTheOneTheCatalogueOwns()
    {
        CustomerSnapshot.ContactPermission.ShouldBe(CustomersPermissions.ReadContact);
    }

    [Fact]
    public void OnlyACallerHoldingTheContactPermissionMayReadContact()
    {
        CustomerSnapshot.MayReadContact([CustomersPermissions.ReadContact]).ShouldBeTrue();
        CustomerSnapshot.MayReadContact(
            [CustomersPermissions.Read, CustomersPermissions.ReadContact]).ShouldBeTrue();

        CustomerSnapshot.MayReadContact([CustomersPermissions.Read]).ShouldBeFalse();
        CustomerSnapshot.MayReadContact([]).ShouldBeFalse();
        CustomerSnapshot.MayReadContact(null).ShouldBeFalse();
    }

    /// <summary>
    /// Permission keys are compared as written. A catalogue that ever admitted a differently-cased key
    /// would be a catalogue with two keys, so the comparison is ordinal and this says so out loud.
    /// </summary>
    [Fact]
    public void APermissionKeyIsMatchedExactlyAsItIsWritten()
    {
        CustomerSnapshot.MayReadContact(["Customers.Read_Contact"]).ShouldBeFalse();
    }
}
