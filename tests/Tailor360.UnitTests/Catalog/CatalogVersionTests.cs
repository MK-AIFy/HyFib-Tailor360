using Shouldly;
using Tailor360.Modules.Catalog.Domain.Catalogue;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// The catalogue version as an aggregate: what it lets an administrator do, and when it stops them.
/// </summary>
/// <remarks>
/// The rules here are the ones a single version can see — codes unique within it, parents that exist,
/// a hierarchy that does not loop, and a lifecycle that runs one way. Everything that needs the other
/// modules or the organisation's history is publish-time validation and is tested beside the validator.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class CatalogVersionTests
{
    [Fact]
    public void StartsAsADraftWithNothingInIt()
    {
        var version = CatalogTestData.Draft();

        version.Status.ShouldBe(CatalogStatus.Draft);
        version.IsEditable.ShouldBeTrue();
        version.Categories.ShouldBeEmpty();
        version.ServiceTypes.ShouldBeEmpty();
        version.PublishedAt.ShouldBeNull();
        version.RetiredAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesADraftWithNoName(string name)
    {
        var created = CatalogVersion.CreateDraft(
            CatalogTestData.Id("v"), CatalogTestData.Organisation, 1, name, null,
            CatalogTestData.Now, null);

        created.IsFailure.ShouldBeTrue();
        created.Error.Code.ShouldBe("catalog.value-required");
    }

    [Fact]
    public void RefusesASecondCategoryWithTheSameCode()
    {
        var version = CatalogTestData.Draft();

        Add(version, "BLOUSE").IsSuccess.ShouldBeTrue();

        var second = Add(version, "BLOUSE");

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("catalog.code-not-unique");
    }

    [Fact]
    public void AllowsTheSameServiceCodeUnderTwoCategories()
    {
        // BLOUSE_PATTERN.STITCHING and SALWAR.STITCHING are different records with different links,
        // which is why a service code is unique within its category rather than across the catalogue
        // (docs/prd/category-hierarchy.md section 3).
        var version = CatalogTestData.Draft();
        var blouse = Add(version, "BLOUSE_PATTERN").Value;
        var salwar = Add(version, "SALWAR").Value;

        AddService(version, blouse.Id, "STITCHING").IsSuccess.ShouldBeTrue();
        AddService(version, salwar.Id, "STITCHING").IsSuccess.ShouldBeTrue();

        version.ServiceTypes.Count.ShouldBe(2);
    }

    [Fact]
    public void RefusesASecondServiceWithTheSameCodeUnderOneCategory()
    {
        var version = CatalogTestData.Draft();
        var blouse = Add(version, "BLOUSE").Value;

        AddService(version, blouse.Id, "STITCHING").IsSuccess.ShouldBeTrue();

        var second = AddService(version, blouse.Id, "STITCHING");

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("catalog.code-not-unique");
    }

    [Fact]
    public void RefusesAParentThisVersionDoesNotHold()
    {
        var version = CatalogTestData.Draft();

        var added = version.AddCategory(
            CatalogTestData.Id("orphan"),
            CatalogTestData.Id("orphan-key"),
            CatalogTestData.Id("somebody-elses-category"),
            CatalogTestData.CategoryOf("BLOUSE_AARI"),
            CatalogTestData.Now,
            null);

        added.IsFailure.ShouldBeTrue();
        added.Error.Code.ShouldBe("catalog.parent-not-found");
    }

    [Fact]
    public void RefusesAReparentingThatWouldMakeTheHierarchyCircular()
    {
        var version = CatalogTestData.Draft();
        var parent = Add(version, "BLOUSE").Value;
        var child = Add(version, "BLOUSE_AARI", parentId: parent.Id).Value;

        var moved = version.EditCategory(
            parent.Id, child.Id, CatalogTestData.CategoryOf("BLOUSE"), CatalogTestData.Now, null);

        moved.IsFailure.ShouldBeTrue();
        moved.Error.Code.ShouldBe("catalog.hierarchy-would-cycle");
    }

    [Fact]
    public void RefusesACategoryThatWouldBeItsOwnParent()
    {
        var version = CatalogTestData.Draft();
        var category = Add(version, "BLOUSE").Value;

        var moved = version.EditCategory(
            category.Id, category.Id, CatalogTestData.CategoryOf("BLOUSE"), CatalogTestData.Now, null);

        moved.IsFailure.ShouldBeTrue();
        moved.Error.Code.ShouldBe("catalog.hierarchy-would-cycle");
    }

    [Fact]
    public void RemovingACategoryTakesItsDescendantsAndTheirServicesWithIt()
    {
        // Leaving them would produce the orphaned parent publish validation refuses, discovered
        // minutes later by somebody who did not make the change.
        var version = CatalogTestData.Draft();
        var blouse = Add(version, "BLOUSE").Value;
        var pattern = Add(version, "BLOUSE_PATTERN", parentId: blouse.Id).Value;
        var salwar = Add(version, "SALWAR").Value;

        AddService(version, pattern.Id, "STITCHING");
        AddService(version, salwar.Id, "STITCHING");

        version.RemoveCategory(blouse.Id, CatalogTestData.Now, null).IsSuccess.ShouldBeTrue();

        version.Categories.Select(category => category.Code).ShouldBe(["SALWAR"]);
        version.ServiceTypes.Count.ShouldBe(1);
        version.ServiceTypes.Single().CategoryId.ShouldBe(salwar.Id);
    }

    [Fact]
    public void IsAGroupingNodeExactlyWhenSomethingNamesItAsAParent()
    {
        var version = CatalogTestData.Draft();
        var blouse = Add(version, "BLOUSE").Value;
        var salwar = Add(version, "SALWAR").Value;

        version.IsGroupingNode(blouse.Id).ShouldBeFalse();

        Add(version, "BLOUSE_PATTERN", parentId: blouse.Id);

        version.IsGroupingNode(blouse.Id).ShouldBeTrue();
        version.IsGroupingNode(salwar.Id).ShouldBeFalse();
    }

    [Fact]
    public void PublishesADraftAndKnowsWhichServicesMayBeOrdered()
    {
        var version = CatalogTestData.Draft();
        var category = Add(version, "BLOUSE").Value;

        var complete = AddService(version, category.Id, "STITCHING").Value;
        var incomplete = version.AddServiceType(
            CatalogTestData.Id("incomplete"),
            CatalogTestData.Id("incomplete-key"),
            category.Id,
            CatalogTestData.ServiceOf("ALTERATION", complete: false, allowIncomplete: true),
            CatalogTestData.Now,
            null).Value;

        // Derived from the links rather than settled at publication, so it reads the same before and
        // after — which is what lets a published version stay immutable, because publishing writes
        // nothing to the service's row.
        complete.NotOrderable.ShouldBeFalse();
        incomplete.NotOrderable.ShouldBeTrue();

        version.Publish(CatalogTestData.Now, null, "Launch").IsSuccess.ShouldBeTrue();

        version.Status.ShouldBe(CatalogStatus.Published);
        version.PublishedAt.ShouldBe(CatalogTestData.Now);
        version.PublishReason.ShouldBe("Launch");
        complete.NotOrderable.ShouldBeFalse();
        incomplete.NotOrderable.ShouldBeTrue(
            "a service published with a link missing is configuration nobody can order against");
    }

    [Fact]
    public void RefusesToPublishWithoutAReason()
    {
        var version = CatalogTestData.Draft();

        var published = version.Publish(CatalogTestData.Now, null, "  ");

        published.IsFailure.ShouldBeTrue();
        published.Error.Code.ShouldBe("catalog.value-required");
        version.Status.ShouldBe(CatalogStatus.Draft);
    }

    [Fact]
    public void RefusesEveryEditOnceTheVersionIsPublished()
    {
        var version = CatalogTestData.Draft();
        var category = Add(version, "BLOUSE").Value;

        version.Publish(CatalogTestData.Now, null, "Launch");

        Add(version, "SALWAR").Error.Code.ShouldBe("catalog.version-not-editable");
        version.EditCategory(category.Id, null, CatalogTestData.CategoryOf("BLOUSE"),
            CatalogTestData.Now, null).Error.Code.ShouldBe("catalog.version-not-editable");
        version.RemoveCategory(category.Id, CatalogTestData.Now, null)
            .Error.Code.ShouldBe("catalog.version-not-editable");
        AddService(version, category.Id, "STITCHING").Error.Code.ShouldBe("catalog.version-not-editable");
    }

    [Fact]
    public void RefusesToPublishTwice()
    {
        var version = CatalogTestData.Draft();

        version.Publish(CatalogTestData.Now, null, "Launch").IsSuccess.ShouldBeTrue();

        version.Publish(CatalogTestData.Now, null, "Again")
            .Error.Code.ShouldBe("catalog.version-not-publishable");
    }

    [Fact]
    public void RetiresOnlyAPublishedVersion()
    {
        var draft = CatalogTestData.Draft();

        draft.Retire(CatalogTestData.Now, null, "No").Error.Code.ShouldBe("catalog.version-not-retirable");

        draft.Publish(CatalogTestData.Now, null, "Launch");
        draft.Retire(CatalogTestData.Now, null, "Superseded").IsSuccess.ShouldBeTrue();

        draft.Status.ShouldBe(CatalogStatus.Retired);
        draft.RetiredReason.ShouldBe("Superseded");

        draft.Retire(CatalogTestData.Now, null, "Again")
            .Error.Code.ShouldBe("catalog.version-not-retirable");
    }

    [Fact]
    public void CorrectsPresentationOnAPublishedVersionAndOnlyThere()
    {
        var version = CatalogTestData.Draft();
        var category = Add(version, "BLOUSE").Value;
        var correction = new CatalogPresentation("Blouse — Pattern cut", "ரவிக்கை", "Saree blouses", 3);

        version.CorrectCategoryPresentation(category.Id, correction, CatalogTestData.Now, null)
            .Error.Code.ShouldBe("catalog.correction-needs-published-version");

        version.Publish(CatalogTestData.Now, null, "Launch");

        var corrected = version.CorrectCategoryPresentation(
            category.Id, correction, CatalogTestData.Now, null);

        corrected.IsSuccess.ShouldBeTrue();
        corrected.Value.Name.ShouldBe("Blouse — Pattern cut");
        corrected.Value.NameTamil.ShouldBe("ரவிக்கை");
        corrected.Value.DisplayOrder.ShouldBe(3);
        corrected.Value.Code.ShouldBe("BLOUSE", "a correction never touches the code");
    }

    [Fact]
    public void CloningCopiesTheTreeWithFreshRowsAndTheSameConcepts()
    {
        var published = CatalogTestData.Draft();
        var blouse = Add(published, "BLOUSE").Value;
        var pattern = Add(published, "BLOUSE_PATTERN", parentId: blouse.Id).Value;
        var stitching = AddService(published, pattern.Id, "STITCHING").Value;

        published.Publish(CatalogTestData.Now, null, "Launch");

        var clone = published.CloneAsDraft(
            new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null).Value;

        clone.Status.ShouldBe(CatalogStatus.Draft);
        clone.ClonedFromVersionId.ShouldBe(published.Id);
        clone.Categories.Count.ShouldBe(2);
        clone.ServiceTypes.Count.ShouldBe(1);

        var clonedPattern = clone.Categories.Single(category => category.Code == "BLOUSE_PATTERN");
        var clonedBlouse = clone.Categories.Single(category => category.Code == "BLOUSE");
        var clonedStitching = clone.ServiceTypes.Single();

        clonedPattern.Id.ShouldNotBe(pattern.Id, "every version's rows are its own");
        clonedPattern.Key.ShouldBe(pattern.Key, "it is still the same category");
        clonedStitching.Key.ShouldBe(stitching.Key);

        clonedPattern.ParentId.ShouldBe(
            clonedBlouse.Id, "the parent is re-pointed at the copy, not left naming the original");
        clonedStitching.CategoryId.ShouldBe(clonedPattern.Id);
        clonedStitching.MeasurementTemplateId.ShouldBe(stitching.MeasurementTemplateId);
    }

    [Fact]
    public void CloningIsAllowedFromARetiredVersion()
    {
        // Section 7's diagram has an edge from Retired back to Clone, and it earns its keep: bringing
        // back a category the shop stopped offering is a clone of the version that last described it.
        var version = CatalogTestData.Draft();

        Add(version, "BLOUSE");
        version.Publish(CatalogTestData.Now, null, "Launch");
        version.Retire(CatalogTestData.Now, null, "Seasonal");

        var clone = version.CloneAsDraft(
            new CountingCatalogIds(), 2, "Version 2", null, CatalogTestData.Now, null);

        clone.IsSuccess.ShouldBeTrue();
        clone.Value.Categories.Single().Code.ShouldBe("BLOUSE");
    }

    [Fact]
    public void IsActiveOnlyInsideItsActivePeriod()
    {
        var version = CatalogTestData.Draft();
        var category = Add(
            version,
            "SEASONAL",
            details: CatalogTestData.CategoryOf(
                "SEASONAL",
                activeFrom: new DateOnly(2026, 10, 1),
                activeTo: new DateOnly(2026, 10, 31))).Value;

        category.IsActiveOn(new DateOnly(2026, 9, 30)).ShouldBeFalse();
        category.IsActiveOn(new DateOnly(2026, 10, 1)).ShouldBeTrue();
        category.IsActiveOn(new DateOnly(2026, 10, 31)).ShouldBeTrue();
        category.IsActiveOn(new DateOnly(2026, 11, 1)).ShouldBeFalse();
    }

    [Fact]
    public void RefusesAnActivePeriodThatEndsBeforeItStarts()
    {
        var version = CatalogTestData.Draft();

        var added = Add(
            version,
            "SEASONAL",
            details: CatalogTestData.CategoryOf(
                "SEASONAL",
                activeFrom: new DateOnly(2026, 10, 31),
                activeTo: new DateOnly(2026, 10, 1)));

        added.IsFailure.ShouldBeTrue();
        added.Error.Code.ShouldBe("catalog.active-dates-reversed");
    }

    private static Tailor360.Platform.Abstractions.Results.Result<Category> Add(
        CatalogVersion version,
        string code,
        Guid? parentId = null,
        CategoryDetails? details = null)
        => version.AddCategory(
            CatalogTestData.Id($"category-{code}"),
            CatalogTestData.Id($"category-key-{code}"),
            parentId,
            details ?? CatalogTestData.CategoryOf(code),
            CatalogTestData.Now,
            null);

    private static Tailor360.Platform.Abstractions.Results.Result<ServiceType> AddService(
        CatalogVersion version,
        Guid categoryId,
        string code)
        => version.AddServiceType(
            CatalogTestData.Id($"service-{categoryId}-{code}"),
            CatalogTestData.Id($"service-key-{categoryId}-{code}"),
            categoryId,
            CatalogTestData.ServiceOf(code),
            CatalogTestData.Now,
            null);
}
