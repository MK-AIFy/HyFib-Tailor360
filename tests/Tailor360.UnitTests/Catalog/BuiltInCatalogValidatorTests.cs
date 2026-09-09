using Shouldly;
using Tailor360.Modules.Catalog.Application.Abstractions;
using Tailor360.Modules.Catalog.Application.Catalogue;
using Tailor360.Modules.Catalog.Contracts.Catalogue;
using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// The checks the catalogue makes about itself before anybody else's validator is asked.
/// </summary>
/// <remarks>
/// One test per row of section 10 of <c>docs/prd/category-hierarchy.md</c>, and the severity is part
/// of what is asserted: the difference between an error and a warning is the difference between an
/// administrator being stopped and an administrator being told.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class BuiltInCatalogValidatorTests
{
    private readonly BuiltInCatalogValidator _validator = new();

    [Fact]
    public async Task FindsNothingWrongWithAWellFormedCatalogue()
    {
        var findings = await CheckAsync(WellFormed());

        findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task RefusesAnOrphanedParent()
    {
        // Built through the contract types rather than the aggregate, because the aggregate refuses to
        // create one at all. The validator exists for the state a repair script or a partial delete
        // could leave behind, so it has to be given that state directly.
        var candidate = Candidate(
            categories:
            [
                View("BLOUSE_AARI", parentId: CatalogTestData.Id("a-category-that-is-not-here")),
            ],
            serviceTypes: []);

        var findings = await CheckAsync(candidate);

        Errors(findings).ShouldContain(finding => finding.Code == "catalog.orphaned-parent");
    }

    [Fact]
    public async Task RefusesACycleInTheHierarchy()
    {
        var first = CatalogTestData.Id("first");
        var second = CatalogTestData.Id("second");

        var candidate = Candidate(
            categories:
            [
                View("BLOUSE", id: first, parentId: second),
                View("BLOUSE_AARI", id: second, parentId: first),
            ],
            serviceTypes: []);

        var findings = await CheckAsync(candidate);

        Errors(findings).Count(finding => finding.Code == "catalog.hierarchy-cycle").ShouldBe(2);
    }

    [Fact]
    public async Task RefusesTwoCategoriesSharingACode()
    {
        var candidate = Candidate(
            categories:
            [
                View("BLOUSE", id: CatalogTestData.Id("one"), key: CatalogTestData.Id("key-one")),
                View("BLOUSE", id: CatalogTestData.Id("two"), key: CatalogTestData.Id("key-two")),
            ],
            serviceTypes: []);

        var findings = await CheckAsync(candidate);

        Errors(findings).ShouldContain(finding => finding.Code == "catalog.duplicate-category-code");
    }

    [Fact]
    public async Task RefusesACodeThatChangedOnAnAlreadyPublishedCategory()
    {
        var key = CatalogTestData.Id("category-key-BLOUSE");
        var candidate = Candidate(
            categories: [View("BLOUSE_NEW", key: key)],
            serviceTypes: [],
            categoryHistory: new CatalogCodeHistory(
                new Dictionary<Guid, string> { [key] = "BLOUSE" },
                new Dictionary<string, Guid>(StringComparer.Ordinal) { ["BLOUSE"] = key }));

        var findings = await CheckAsync(candidate);

        var finding = Errors(findings)
            .Single(found => found.Code == "catalog.published-code-changed");

        finding.Message.ShouldContain("'BLOUSE'");
        finding.Target.ShouldBe("categories[BLOUSE_NEW].code");
    }

    [Fact]
    public async Task RefusesACodeAlreadyPublishedForSomethingElse()
    {
        // A code is never handed on, even after the thing that carried it is retired: every price
        // list, report and export that mentions it would silently change meaning.
        var somebodyElse = CatalogTestData.Id("the-old-blouse-category");
        var candidate = Candidate(
            categories: [View("BLOUSE", key: CatalogTestData.Id("a-brand-new-concept"))],
            serviceTypes: [],
            categoryHistory: new CatalogCodeHistory(
                new Dictionary<Guid, string> { [somebodyElse] = "BLOUSE" },
                new Dictionary<string, Guid>(StringComparer.Ordinal) { ["BLOUSE"] = somebodyElse }));

        var findings = await CheckAsync(candidate);

        Errors(findings).ShouldContain(finding => finding.Code == "catalog.published-code-reused");
    }

    [Fact]
    public async Task RefusesASubCategoryOfferedWhereItsParentIsNot()
    {
        var parent = CatalogTestData.Id("parent");
        var candidate = Candidate(
            categories:
            [
                View("BLOUSE", id: parent, branches: [CatalogTestData.MainBranch]),
                View("BLOUSE_AARI", parentId: parent,
                    branches: [CatalogTestData.MainBranch, CatalogTestData.SecondBranch]),
            ],
            serviceTypes: []);

        var findings = await CheckAsync(candidate);

        Errors(findings).ShouldContain(
            finding => finding.Code == "catalog.branches-not-subset-of-parent");
    }

    [Fact]
    public async Task RefusesAServiceOfferedWhereItsCategoryIsNot()
    {
        var category = CatalogTestData.Id("category");
        var candidate = Candidate(
            categories: [View("SALWAR", id: category, branches: [CatalogTestData.MainBranch])],
            serviceTypes:
            [
                ServiceView("STITCHING", category,
                    branches: [CatalogTestData.MainBranch, CatalogTestData.SecondBranch]),
            ]);

        var findings = await CheckAsync(candidate);

        Errors(findings).ShouldContain(
            finding => finding.Code == "catalog.branches-not-subset-of-category");
    }

    [Fact]
    public async Task RefusesAServiceTypeOnAGroupingNode()
    {
        // Rule 1 of section 2: orders are placed against BLOUSE_PATTERN and BLOUSE_AARI, never against
        // BLOUSE. A service type on the grouping node could never be ordered.
        var blouse = CatalogTestData.Id("blouse");
        var candidate = Candidate(
            categories:
            [
                View("BLOUSE", id: blouse),
                View("BLOUSE_PATTERN", parentId: blouse),
            ],
            serviceTypes: [ServiceView("STITCHING", blouse)]);

        var findings = await CheckAsync(candidate);

        Errors(findings).ShouldContain(
            finding => finding.Code == "catalog.service-type-on-grouping-node");
    }

    [Fact]
    public async Task RefusesAMissingLinkUnlessTheAdministratorAcceptedIt()
    {
        var category = CatalogTestData.Id("category");
        var refused = Candidate(
            categories: [View("SALWAR", id: category)],
            serviceTypes: [ServiceView("STITCHING", category, complete: false)]);

        var errors = Errors(await CheckAsync(refused));

        errors.ShouldContain(finding => finding.Code == "catalog.service-type-missing-link");
        errors.Single(finding => finding.Code == "catalog.service-type-missing-link")
            .Message.ShouldContain("measurementTemplateId");

        var accepted = Candidate(
            categories: [View("SALWAR", id: category)],
            serviceTypes: [ServiceView("STITCHING", category, complete: false, allowIncomplete: true)]);

        var findings = await CheckAsync(accepted);

        Errors(findings).ShouldBeEmpty();
        Warnings(findings).ShouldContain(finding => finding.Code == "catalog.service-type-incomplete");
    }

    [Fact]
    public async Task WarnsWhenASubCategoryCodeDoesNotCarryItsParentsPrefix()
    {
        var parent = CatalogTestData.Id("parent");
        var candidate = Candidate(
            categories:
            [
                View("BLOUSE", id: parent),
                View("AARI", parentId: parent),
            ],
            serviceTypes: [ServiceView("STITCHING", CatalogTestData.Id("category-AARI"))]);

        var findings = await CheckAsync(candidate);

        Warnings(findings).ShouldContain(
            finding => finding.Code == "catalog.subcategory-code-not-prefixed");
    }

    [Fact]
    public async Task WarnsAboutACategoryOfferedNowhere()
    {
        var candidate = Candidate(
            categories: [View("SALWAR", branches: [])],
            serviceTypes: [ServiceView("STITCHING", CatalogTestData.Id("category-SALWAR"), branches: [])]);

        var findings = await CheckAsync(candidate);

        Errors(findings).ShouldBeEmpty("an empty set of branches is a warning, not a refusal");
        Warnings(findings).ShouldContain(finding => finding.Code == "catalog.category-offered-nowhere");
    }

    [Fact]
    public async Task WarnsAboutACategoryWithNothingToOrder()
    {
        var candidate = Candidate(categories: [View("SALWAR")], serviceTypes: []);

        var findings = await CheckAsync(candidate);

        Warnings(findings).ShouldContain(finding => finding.Code == "catalog.category-has-no-services");
    }

    [Fact]
    public async Task WarnsThatRetiringTheLastPublishedVersionLeavesNothingOrderable()
    {
        var findings = await _validator.ValidateRetirementAsync(
            new CatalogRetirementCandidate(
                CatalogTestData.Id("version"), CatalogTestData.Organisation, null, new HashSet<Guid>()),
            TestContext.Current.CancellationToken);

        findings.ShouldHaveSingleItem()
            .Code.ShouldBe("catalog.retirement-leaves-nothing-published");
        findings.Single().Severity.ShouldBe(CatalogFindingSeverity.Warning);
    }

    [Fact]
    public async Task SaysNothingAboutARetirementThatHasASuccessor()
    {
        var findings = await _validator.ValidateRetirementAsync(
            new CatalogRetirementCandidate(
                CatalogTestData.Id("version"),
                CatalogTestData.Organisation,
                CatalogTestData.Id("successor"),
                new HashSet<Guid> { CatalogTestData.Id("service-key") }),
            TestContext.Current.CancellationToken);

        findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReportsEverythingWrongRatherThanStoppingAtTheFirst()
    {
        // An administrator fixing one code at a time and re-submitting is a far worse afternoon than
        // one report naming everything.
        var candidate = Candidate(
            categories:
            [
                View("BLOUSE", id: CatalogTestData.Id("one"), key: CatalogTestData.Id("key-one")),
                View("BLOUSE", id: CatalogTestData.Id("two"), key: CatalogTestData.Id("key-two")),
                View("SALWAR", id: CatalogTestData.Id("three"), key: CatalogTestData.Id("key-three"),
                    parentId: CatalogTestData.Id("missing")),
            ],
            serviceTypes: [ServiceView("STITCHING", CatalogTestData.Id("three"), complete: false)]);

        var codes = Errors(await CheckAsync(candidate)).Select(finding => finding.Code).ToArray();

        codes.ShouldContain("catalog.duplicate-category-code");
        codes.ShouldContain("catalog.orphaned-parent");
        codes.ShouldContain("catalog.service-type-missing-link");
    }

    private async Task<IReadOnlyList<CatalogFinding>> CheckAsync(CatalogPublicationCandidate candidate)
        => await _validator.ValidatePublicationAsync(candidate, TestContext.Current.CancellationToken);

    private static IReadOnlyList<CatalogFinding> Errors(IEnumerable<CatalogFinding> findings)
        => [.. findings.Where(finding => finding.Severity == CatalogFindingSeverity.Error)];

    private static IReadOnlyList<CatalogFinding> Warnings(IEnumerable<CatalogFinding> findings)
        => [.. findings.Where(finding => finding.Severity == CatalogFindingSeverity.Warning)];

    /// <summary>A catalogue with a grouping node, two sub-categories and complete services.</summary>
    private static CatalogPublicationCandidate WellFormed()
    {
        var version = CatalogTestData.Draft();
        var blouse = version.AddCategory(
            CatalogTestData.Id("blouse"), CatalogTestData.Id("blouse-key"), null,
            CatalogTestData.CategoryOf("BLOUSE"), CatalogTestData.Now, null).Value;

        foreach (var code in new[] { "BLOUSE_PATTERN", "BLOUSE_AARI" })
        {
            var child = version.AddCategory(
                CatalogTestData.Id(code), CatalogTestData.Id($"{code}-key"), blouse.Id,
                CatalogTestData.CategoryOf(code), CatalogTestData.Now, null).Value;

            version.AddServiceType(
                CatalogTestData.Id($"{code}-stitching"),
                CatalogTestData.Id($"{code}-stitching-key"),
                child.Id,
                CatalogTestData.ServiceOf("STITCHING"),
                CatalogTestData.Now,
                null);
        }

        return CatalogProjection.ToCandidate(version, CatalogCodeLedger.Empty);
    }

    private static CatalogPublicationCandidate Candidate(
        IReadOnlyList<CatalogCategoryView> categories,
        IReadOnlyList<CatalogServiceTypeView> serviceTypes,
        CatalogCodeHistory? categoryHistory = null,
        CatalogCodeHistory? serviceHistory = null)
        => new(
            CatalogTestData.Id("version"),
            CatalogTestData.Organisation,
            1,
            categories,
            serviceTypes,
            categoryHistory ?? CatalogCodeHistory.Empty,
            serviceHistory ?? CatalogCodeHistory.Empty);

    private static CatalogCategoryView View(
        string code,
        Guid? id = null,
        Guid? key = null,
        Guid? parentId = null,
        IReadOnlyCollection<Guid>? branches = null)
        => new(
            id ?? CatalogTestData.Id($"category-{code}"),
            key ?? CatalogTestData.Id($"category-key-{code}"),
            code,
            code,
            parentId,
            null,
            null,
            null,
            branches ?? [CatalogTestData.MainBranch]);

    private static CatalogServiceTypeView ServiceView(
        string code,
        Guid categoryId,
        bool complete = true,
        bool allowIncomplete = false,
        IReadOnlyCollection<Guid>? branches = null)
        => new(
            CatalogTestData.Id($"service-{categoryId}-{code}"),
            CatalogTestData.Id($"service-key-{categoryId}-{code}"),
            categoryId,
            code,
            code,
            complete ? CatalogTestData.Id($"template-{code}") : null,
            complete ? CatalogTestData.Id($"workflow-{code}") : null,
            [],
            complete ? $"PL-{code}" : null,
            complete ? CatalogTestData.Id($"checklist-{code}") : null,
            allowIncomplete,
            null,
            null,
            branches ?? [CatalogTestData.MainBranch]);
}
