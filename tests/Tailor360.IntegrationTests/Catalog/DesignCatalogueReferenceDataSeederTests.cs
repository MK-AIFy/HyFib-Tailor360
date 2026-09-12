using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.IntegrationTests.Catalog;

/// <summary>
/// <see cref="IDesignCatalogueReferenceDataSeeder"/> against a real database (issue #139, following
/// #137 and #138): idempotency, the full publication check the document predicts zero design findings
/// for, the service-type link (<c>STITCHING</c>/<c>RESTITCHING</c> carry the groups, <c>ALTERATION</c>
/// none — OD-DES-01), and idempotency again once the draft is published.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WebApplicationCollection.Name)]
public sealed class DesignCatalogueReferenceDataSeederTests(WebApplicationFixture fixture)
{
    [Fact]
    public async Task SeedsFiftySixGroupsAndFortyFourRulesIdempotentlyAndAgainAfterPublication()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        // A fresh organisation per test, so this exercises the seeder against what is, for it, an empty
        // database — no catalogue version it has ever seen — without disturbing any other test's data.
        var organisationId = Guid.CreateVersion7();

        using var scope = fixture.Services.CreateScope();
        var hierarchySeeder = scope.ServiceProvider.GetRequiredService<ICatalogReferenceDataSeeder>();
        var designSeeder = scope.ServiceProvider.GetRequiredService<IDesignCatalogueReferenceDataSeeder>();
        var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var check = scope.ServiceProvider.GetRequiredService<CatalogPublicationCheck>();

        var hierarchy = await hierarchySeeder.SeedInitialCatalogAsync(organisationId, Token);
        hierarchy.Created.ShouldBeTrue();

        var first = await designSeeder.SeedDesignGroupsAsync(organisationId, Token);
        first.Created.ShouldBeTrue();
        first.CatalogVersionId.ShouldBe(hierarchy.CatalogVersionId);
        first.GroupCount.ShouldBe(56, "section 1: 56 groups across the six orderable categories");
        first.RuleCount.ShouldBe(44, "section 1: 44 rules, DR-01 to DR-44");

        // Run again: adds nothing and reports what exists, as the category seed does today.
        var second = await designSeeder.SeedDesignGroupsAsync(organisationId, Token);
        second.Created.ShouldBeFalse("a second run must add nothing and report what already exists");
        second.CatalogVersionId.ShouldBe(first.CatalogVersionId);
        second.GroupCount.ShouldBe(56);
        second.RuleCount.ShouldBe(44);

        // Idempotency proven at the row level too, not only through the reported outcome.
        var expectedOptionCount = SeededDesignCatalogue.Groups.Sum(group => group.Options.Count);
        // Not disposed here: it is resolved from the same scope everything else in this test uses, and
        // the scope — not this method — owns its lifetime.
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (await context.DesignGroups
                .CountAsync(group => group.CatalogVersionId == first.CatalogVersionId, Token))
            .ShouldBe(56);
        (await context.DesignOptions
                .CountAsync(option => option.CatalogVersionId == first.CatalogVersionId, Token))
            .ShouldBe(expectedOptionCount);
        (await context.DesignRules
                .CountAsync(rule => rule.CatalogVersionId == first.CatalogVersionId, Token))
            .ShouldBe(44);

        // The full publication check of #137/#138: no error, and the design-scoped findings are exactly
        // what section 10 predicts for this seed — none, because every option carries a bundled
        // illustration key and alt text, and no group offers more than the proposed twelve-option ceiling.
        var draft = await store.FindAsync(first.CatalogVersionId, organisationId, Token);
        draft.ShouldNotBeNull();

        var report = await check.RunAsync(draft!, Token);
        report.IsSuccess.ShouldBeTrue();

        var designFindings = report.Value.Findings
            .Where(found => found.Finding.Code.Contains("design", StringComparison.Ordinal))
            .ToArray();
        designFindings.ShouldBeEmpty(
            "the design-scoped findings the document's section 10 predicts for this seed: "
            + string.Join("; ", designFindings.Select(
                found => $"{found.Validator}/{found.Finding.Severity} {found.Finding.Code}: {found.Finding.Message}")));

        // Once more after publication. Published directly (bypassing the other four service-type links
        // #27, #33, #41 and #34 have not landed yet, which this issue does not touch) so that the seeder
        // is exercised against a version that is no longer editable, and must still recognise it has
        // already run rather than attempt — and fail — a domain mutation.
        draft!.Publish(clock.UtcNow, null, "Test publish, to prove the design seeder is idempotent afterwards.")
            .IsSuccess.ShouldBeTrue();
        await store.SaveAsync(Token);

        var third = await designSeeder.SeedDesignGroupsAsync(organisationId, Token);
        third.Created.ShouldBeFalse("a run against a published version must still recognise it is already seeded");
        third.CatalogVersionId.ShouldBe(first.CatalogVersionId);
        third.GroupCount.ShouldBe(56);
        third.RuleCount.ShouldBe(44);
    }

    [Fact]
    public async Task LinksOnlyStitchingAndRestitchingToTheDesignGroupsAndNeverAlteration()
    {
        Assert.SkipUnless(DatabaseAvailability.IsAvailable, DatabaseAvailability.SkipReason);

        var organisationId = Guid.CreateVersion7();

        using var scope = fixture.Services.CreateScope();
        var hierarchySeeder = scope.ServiceProvider.GetRequiredService<ICatalogReferenceDataSeeder>();
        var designSeeder = scope.ServiceProvider.GetRequiredService<IDesignCatalogueReferenceDataSeeder>();
        var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();

        await hierarchySeeder.SeedInitialCatalogAsync(organisationId, Token);
        var outcome = await designSeeder.SeedDesignGroupsAsync(organisationId, Token);

        var draft = await store.FindAsync(outcome.CatalogVersionId, organisationId, Token);
        draft.ShouldNotBeNull();

        foreach (var category in draft!.Categories)
        {
            var expectedGroupCount = SeededDesignCatalogue.Groups
                .Count(group => group.CategoryCode == category.Code);

            foreach (var service in draft.ServicesOf(category.Id))
            {
                if (service.Code == "ALTERATION")
                {
                    service.DesignOptionGroupIds.ShouldBeEmpty(
                        $"{category.Code}.ALTERATION must carry no design groups (Section 9, OD-DES-01)");
                }
                else
                {
                    service.DesignOptionGroupIds.Count().ShouldBe(
                        expectedGroupCount, $"{category.Code}.{service.Code} must carry the full group set");
                }
            }
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
