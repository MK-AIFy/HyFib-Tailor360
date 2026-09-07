using System.Net;
using Shouldly;

namespace Tailor360.IntegrationTests.Identity;

/// <summary>
/// Reading and exporting the audit trail.
/// </summary>
/// <remarks>
/// This is the half of "every administrative mutation has reviewable audit evidence" that somebody
/// uses. The commands elsewhere already write their before-and-after entries; what these tests assert
/// is that a reviewer can find them, that the trail says what changed rather than only what it is now,
/// and that taking a copy out of the system is itself recorded.
/// </remarks>
[Collection(WebApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuditAdministrationEndpointTests(WebApplicationFixture fixture)
{
    private const string Reason = "Quarterly access review requested by the owner.";

    /// <summary>Whether a PostgreSQL instance was found for this run.</summary>
    public static bool Available => DatabaseAvailability.IsAvailable;

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(AuditAdministrationEndpointTests))]
    public async Task TheTrailShowsWhatOneAccountsChangeWasBeforeAndAfter()
    {
        // Somebody suspends an account, and somebody else comes to find out what happened to it. Two
        // administrators, because that is the shape of a review.
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-audited", "203.0.113.120", Permissions.Users);

        using var auditor = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-auditor", "203.0.113.121", Permissions.AuditRead);

        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-audited");

        var read = await administrator.GetAsync($"/api/v1/admin/users/{subject.Id}");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        var suspended = await administrator.PostAsync(
            $"/api/v1/admin/users/{subject.Id}/suspend",
            new { reason = "Left the company on 5 September." },
            ("If-Match", read.Headers.ETag.ShouldNotBeNull().Tag),
            ("Idempotency-Key", Guid.CreateVersion7().ToString()));

        suspended.StatusCode.ShouldBe(HttpStatusCode.OK);

        var trail = await ReadAsync(auditor, $"?entityType=StaffUser&entityId={subject.Id}&limit=50");

        trail.Entries.ShouldContain(e => e.Action == "identity.user.suspended");
        var entry = trail.Entries.First(e => e.Action == "identity.user.suspended");
        entry.Reason.ShouldBe("Left the company on 5 September.");
        entry.ActorId.ShouldBe(administrator.UserId);
        entry.CorrelationId.ShouldNotBeNullOrWhiteSpace(
            "an entry has to join to the request's log lines, or an investigation stops at the trail");

        // The point of a before and an after: the entry says what changed, not only what it is now.
        entry.Before.ShouldNotBeNull().ShouldContain("Active");
        entry.After.ShouldNotBeNull().ShouldContain("Suspended");

        // Naming the identifier without saying what kind of thing it is would work and would use no
        // index, so it is refused rather than quietly scanning the table.
        (await auditor.GetAsync($"/api/v1/admin/audit/?entityId={subject.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(AuditAdministrationEndpointTests))]
    public async Task PagingTheTrailReturnsEveryEntryOnceWhileItIsStillBeingWrittenTo()
    {
        using var administrator = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-page", "203.0.113.122", Permissions.Users);

        using var auditor = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-pageaudit", "203.0.113.123", Permissions.AuditRead);

        var subject = await AuthenticationTestData.CreateSignInReadyUserAsync(fixture, "sub-page");

        // Four entries about one account, so the page boundary falls inside them.
        foreach (var segment in (string[])["suspend", "reinstate", "suspend", "reinstate"])
        {
            var current = await administrator.GetAsync($"/api/v1/admin/users/{subject.Id}");
            current.StatusCode.ShouldBe(HttpStatusCode.OK);

            (await administrator.PostAsync(
                    $"/api/v1/admin/users/{subject.Id}/{segment}",
                    new { reason = Reason },
                    ("If-Match", current.Headers.ETag.ShouldNotBeNull().Tag),
                    ("Idempotency-Key", Guid.CreateVersion7().ToString())))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var seen = new List<long>();
        string? cursor = null;
        var appended = 0;

        do
        {
            var query = $"?entityType=StaffUser&entityId={subject.Id}&limit=2"
                        + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");

            var page = await ReadAsync(auditor, query);
            page.Entries.Count.ShouldBeLessThanOrEqualTo(2);

            seen.AddRange(page.Entries.Select(entry => entry.Sequence));
            cursor = page.NextCursor;

            // The trail is appended to while it is read — that is the normal case, and the reason
            // paging is keyset on a monotonic sequence rather than by offset, which would shift under
            // every page and silently skip entries an investigation needed.
            if (cursor is not null && appended < 2)
            {
                var current = await administrator.GetAsync($"/api/v1/admin/users/{subject.Id}");

                await administrator.PostAsync(
                    $"/api/v1/admin/users/{subject.Id}/revoke-sessions",
                    new { reason = Reason },
                    ("If-Match", current.Headers.ETag.ShouldNotBeNull().Tag),
                    ("Idempotency-Key", Guid.CreateVersion7().ToString()));

                appended++;
            }
        }
        while (cursor is not null);

        seen.Count.ShouldBeGreaterThanOrEqualTo(4);
        seen.Distinct().Count().ShouldBe(seen.Count, "no entry is returned on two pages");
        seen.ShouldBe([.. seen.OrderByDescending(sequence => sequence)], "the trail reads newest first");
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(AuditAdministrationEndpointTests))]
    public async Task TakingACopyOfTheTrailIsItselfRecorded()
    {
        using var exporter = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-export", "203.0.113.124", Permissions.AuditExport);

        using var auditor = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-exportaudit", "203.0.113.125", Permissions.AuditRead);

        var exported = await exporter.PostAsync(
            "/api/v1/admin/audit/export",
            new { entityType = "StaffUser", action = "identity.user.", limit = 10, reason = Reason });

        exported.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await exported.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // The export is a read, and it is audited anyway: taking the record of what everybody did out
        // of the system is the act itself, so the trail says who took it and why.
        var trail = await ReadAsync(
            auditor, $"?entityType=AuditTrail&entityId={exporter.UserId}&limit=10");

        trail.Entries.ShouldContain(e => e.Action == "platform.audit.exported");
        var entry = trail.Entries.First(e => e.Action == "platform.audit.exported");
        entry.Reason.ShouldBe(Reason);
        entry.ActorId.ShouldBe(exporter.UserId);

        // An export with no reason is refused, like every other act on this surface that demands one.
        (await exporter.PostAsync(
                "/api/v1/admin/audit/export", new { entityType = "StaffUser", limit = 10 }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact(Skip = DatabaseAvailability.SkipMessage,
        SkipUnless = nameof(Available),
        SkipType = typeof(AuditAdministrationEndpointTests))]
    public async Task ReadingTheTrailDoesNotLetSomebodyExportIt()
    {
        using var auditor = await AdministrationHarness.AdministratorAsync(
            fixture, "adm-readonly", "203.0.113.126", Permissions.AuditRead);

        // Reading and taking a copy away are different acts with different permissions, and holding the
        // first must not confer the second.
        (await auditor.GetAsync("/api/v1/admin/audit/?limit=5")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await auditor.PostAsync(
                "/api/v1/admin/audit/export", new { limit = 10, reason = Reason }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<AuditPageBody> ReadAsync(
        AdministrationHarness.AdministratorClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/admin/audit/{query}");

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await AuthenticationClient.ReadAsync<AuditPageBody>(response)).ShouldNotBeNull();
    }

    private sealed record AuditPageBody(IReadOnlyList<AuditEntryBody> Entries, string? NextCursor);

    private sealed record AuditEntryBody(
        long Sequence,
        string Action,
        Guid? ActorId,
        string? CorrelationId,
        string? Reason,
        string? Before,
        string? After);
}
