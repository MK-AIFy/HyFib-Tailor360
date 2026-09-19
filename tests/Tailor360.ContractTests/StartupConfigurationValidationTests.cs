using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Tailor360.Web;

namespace Tailor360.ContractTests;

/// <summary>
/// That a configuration value the host cannot honour stops it before it serves its first request.
/// </summary>
/// <remarks>
/// <para>
/// The host declares its options with <c>ValidateDataAnnotations().ValidateOnStart()</c>, which is the
/// mechanism behind issue #21's "invalid or missing required configuration fails safely before serving
/// traffic". Registering the mechanism is not the same as proving it fires: a misplaced
/// <c>ValidateOnStart()</c>, an option bound without annotations, or a validator registered after the
/// host is built all leave the call sites looking correct while the deployment starts anyway and serves
/// requests against a value nobody sanctioned.
/// </para>
/// <para>
/// The distinction that matters operationally is <em>refusing</em> against <em>degrading</em>. A host that
/// starts with an unusable batch limit answers requests, passes its health probe and is rolled forward
/// across the fleet; one that refuses to start fails the deployment while the previous version is still
/// running. These tests assert the refusal, and the control below asserts that the refusal is caused by
/// the value under test rather than by anything missing from the environment the tests run in.
/// </para>
/// </remarks>
[Trait("Category", "Contract")]
public sealed class StartupConfigurationValidationTests
{
    /// <summary>
    /// Each case is a value the annotations on the options type reject. They are supplied the way a
    /// deployment supplies them — as configuration keys, not as a constructed options instance — so the
    /// test covers the binding and the validation together rather than the validator alone.
    /// </summary>
    [Theory]
    [InlineData("ClientTelemetry:MaxEventsPerBatch", "0")]
    [InlineData("ClientTelemetry:MaxEventsPerBatch", "501")]
    [InlineData("ClientTelemetry:MaxBodyBytes", "1023")]
    [InlineData("ClientTelemetry:MaxBodyBytes", "1048577")]
    public void AnOutOfRangeSettingStopsTheHostBeforeItServes(string key, string value)
    {
        using var host = new MisconfiguredHost(new Dictionary<string, string?> { [key] = value });

        var failure = Should.Throw<OptionsValidationException>(
            () => _ = host.Services,
            $"'{key}={value}' is outside the range the options type declares, so the host must refuse to "
            + "start. A host that starts instead serves requests against a limit nobody sanctioned, and "
            + "does so while reporting healthy.");

        failure.Message.ShouldContain(
            key.Split(':')[^1],
            Case.Insensitive,
            "The startup failure has to name the setting that caused it. An operator reads this message "
            + "out of a crash loop with no debugger attached.");
    }

    /// <summary>
    /// The control. Without it, a passing theory above proves only that this environment cannot start a
    /// host at all — these tests run with no database reachable, and a host that refused for that reason
    /// would satisfy every assertion above for the wrong reason.
    /// </summary>
    [Fact]
    public void TheSameHostStartsWhenTheSettingsAreInRange()
    {
        using var host = new MisconfiguredHost(new Dictionary<string, string?>
        {
            ["ClientTelemetry:MaxEventsPerBatch"] = "50",
            ["ClientTelemetry:MaxBodyBytes"] = "32768",
        });

        Should.NotThrow(
            () => _ = host.Services,
            "The refusals above must be attributable to the value under test, not to the environment.");
    }

    /// <summary>
    /// Builds the real host with one or more configuration values overridden, so that what is exercised
    /// is the application's own option registrations rather than a re-declaration of them here.
    /// </summary>
    private sealed class MisconfiguredHost(IDictionary<string, string?> settings)
        : WebApplicationFactory<WebEntryPoint>
    {
        private readonly IDictionary<string, string?> _settings = settings;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Development);

            // Host configuration, for the same reason WebHostFixture uses it: it is in place before the
            // options are bound, which is where a deployment's own values arrive.
            builder.ConfigureHostConfiguration(configuration =>
            {
                if (Environment.GetEnvironmentVariable("TAILOR360_TEST_DATABASE_URL") is { Length: > 0 } url)
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Database:ConnectionString"] = url });
                }

                configuration.AddInMemoryCollection(this._settings);
            });

            return base.CreateHost(builder);
        }
    }
}
