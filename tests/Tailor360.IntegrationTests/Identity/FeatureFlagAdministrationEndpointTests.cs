using System.Net;
using Shouldly;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// Feature flags and module toggles through the administrative surface.
/// </summary>
/// <remarks>
/// Who may reach these is asserted by the role matrix, which now answers the vendor super-user role
/// 200 where it used to answer 403 — the reach gap #25 closed. What is asserted here is what the
/// endpoints do once reached, and in particular the one place their precondition rule differs from
/// every other administrative endpoint: a flag that has never been configured has no version, so
/// demanding one would ask an administrator for a value that does not exist.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FeatureFlagAdministrationEndpointTests(WebApplicationFixture fixture)
{
    private const string Reason = "Enabling the pilot for the Madurai counter from Monday.";

    /// <summary>Whether a PostgreSQL instance was found for this run.</summary>
    public static bool Available => DatabaseAvailability.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(FeatureFlagAdministrationEndpointTests))]
    public async Task AFlagIsConfiguredWithoutAPreconditionAndEditedWithOne()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-flag", "203.0.113.110", Permissions.FeatureFlags);

        var key = $"pilot.{AdministrationHarness.UniqueToken(8)}";

        // Never configured: there is nothing to have changed underneath the administrator, so no
        // If-Match is sent and none is demanded.
        var created = await administrator.PutAsync(
            $"/api/v1/admin/feature-flags/{key}",
            new { enabled = true, reason = Reason },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        created.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var flag = (await AuthenticationClient.ReadAsync<FlagBody>(created)).ShouldNotBeNull();
        flag.Key.ShouldBe(key);
        flag.Enabled.ShouldBeTrue();
        flag.Revision.ShouldBe(1);
        flag.Reason.ShouldBe(Reason);

        // The screen is told how long the other tills take to agree, so an administrator who has just
        // moved a toggle does not move it again wondering why nothing happened.
        flag.PropagationSeconds.ShouldBeGreaterThan(0);

        var version = created.Headers.ETag.ShouldNotBeNull().Tag;

        var edited = await administrator.PutAsync(
            $"/api/v1/admin/feature-flags/{key}",
            new { enabled = false, reason = "Pausing the pilot until the stock count." },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AuthenticationClient.ReadAsync<FlagBody>(edited)).ShouldNotBeNull().Enabled.ShouldBeFalse();

        // The version the first edit consumed is now stale, and a second edit against it is refused
        // rather than silently reversing what the first one decided.
        var stale = await administrator.PutAsync(
            $"/api/v1/admin/feature-flags/{key}",
            new { enabled = true, reason = Reason },
            ("If-Match", version),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(FeatureFlagAdministrationEndpointTests))]
    public async Task AModuleToggleIsAFlagUnderAReservedKeyAndAnUnknownModuleIsRefused()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-module", "203.0.113.111", Permissions.FeatureFlags);

        var toggled = await administrator.PutAsync(
            "/api/v1/admin/feature-flags/modules/Inventory",
            new { enabled = false, reason = "Inventory is not in use until the November stock count." },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        toggled.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await toggled.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // One store, not two: the toggle is a flag whose key carries the reserved prefix, so it obeys
        // the same propagation rule and appears in the same list as everything else.
        (await AuthenticationClient.ReadAsync<FlagBody>(toggled)).ShouldNotBeNull()
            .Key.ShouldBe("module.Inventory");

        var unknown = await administrator.PutAsync(
            "/api/v1/admin/feature-flags/modules/Alterations",
            new { enabled = false, reason = Reason },
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        unknown.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest, "a toggle for a module that does not exist is a switch nothing reads");

        (await AuthenticationClient.CodeAsync(unknown)).ShouldBe("identity.module-not-recognised");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(FeatureFlagAdministrationEndpointTests))]
    public async Task AReadOfAFlagThatWasNeverConfiguredSaysSoRatherThanReportingItOff()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-flagread", "203.0.113.112", Permissions.FeatureFlags);

        var missing = await administrator.GetAsync(
            $"/api/v1/admin/feature-flags/never.{AdministrationHarness.UniqueToken(8)}");

        // Evaluation treats an unknown flag as off, which is the safe answer for a feature that is not
        // yet configured. Administration must not: "off" and "nobody has decided" are different facts,
        // and a screen that showed the first for the second would invite somebody to leave it alone.
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthenticationClient.CodeAsync(missing)).ShouldBe("platform.feature-flag-not-configured");
    }

    private sealed record FlagBody(
        string Key, bool Enabled, string? Reason, int Revision, int PropagationSeconds, string Version);
}
