using Serilog.Events;
using Shouldly;
using Tailor360.Platform.Observability.Logging;

namespace Tailor360.UnitTests.Platform;

/// <summary>
/// Logs are collected by tooling with a wider audience than the database, so a secret reaching a log
/// line is a disclosure. These tests pin the redaction behaviour.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LogRedactionTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("PasswordHash")]
    [InlineData("api_key")]
    [InlineData("ApiKey")]
    [InlineData("APIKEY")]
    [InlineData("Authorization")]
    [InlineData("recovery-codes")]
    [InlineData("MFA_Secret")]
    [InlineData("ConnectionString")]
    [InlineData("dataProtectionKey")]
    [InlineData("webhook.secret")]
    [InlineData("Reference")]
    [InlineData("provider_reference")]
    [InlineData("PaymentReference")]
    [InlineData("payer")]
    public void RecognisesSensitivePropertyNamesInAnyCasingOrSeparatorStyle(string name)
        => LogRedaction.IsSensitive(name).ShouldBeTrue();

    [Theory]
    [InlineData("CustomerId")]
    [InlineData("OrderNumber")]
    [InlineData("BranchId")]
    [InlineData("")]
    public void LeavesOrdinaryPropertyNamesAlone(string name)
        => LogRedaction.IsSensitive(name).ShouldBeFalse();

    [Fact]
    public void ReplacesTheValueOfASensitivePropertyOnTheLogEvent()
    {
        var logEvent = new LogEvent(
            new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero),
            LogEventLevel.Information,
            exception: null,
            new MessageTemplate([]),
            [
                new LogEventProperty("ApiKey", new ScalarValue("sk-live-not-a-real-key")),
                new LogEventProperty("CustomerId", new ScalarValue("c-1")),
            ]);

        new RedactingEnricher().Enrich(logEvent, new NullPropertyFactory());

        logEvent.Properties["ApiKey"].ToString().ShouldContain(LogRedaction.Placeholder);
        logEvent.Properties["CustomerId"].ToString().ShouldContain("c-1");
    }

    [Fact]
    public void MasksPhoneNumbersKeepingOnlyTheLastFourDigits()
    {
        LogRedaction.MaskPhone("9876543210").ShouldBe("******3210");
        LogRedaction.MaskPhone("123").ShouldBe(LogRedaction.Placeholder);
    }

    [Fact]
    public void MasksEmailAddressesKeepingTheDomain()
    {
        LogRedaction.MaskEmail("radhika@example.in").ShouldBe("r***@example.in");
        LogRedaction.MaskEmail("not-an-email").ShouldBe(LogRedaction.Placeholder);
    }

    [Fact]
    public void ScrubsBearerTokensFromFreeText()
    {
        var scrubbed = LogRedaction.ScrubFreeText("upstream said Bearer abcdefghijklmnopqrstuvwxyz012345");

        scrubbed.ShouldNotContain("abcdefghijklmnop");
        scrubbed.ShouldContain(LogRedaction.Placeholder);
    }

    private sealed class NullPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, new ScalarValue(value));
    }
}
