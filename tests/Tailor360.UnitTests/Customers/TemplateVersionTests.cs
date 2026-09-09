using Shouldly;
using Tailor360.Modules.Customers.Domain.Measurements;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The lifecycle a template version moves through, and what it refuses at each point.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TemplateVersionTests
{
    [Fact]
    public void ADraftIsEditedAndAPublishedVersionIsNot()
    {
        var (template, draft) = MeasurementTestData.WithDraft();
        var field = draft.AddField(
            MeasurementTestData.Id("field"), MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Now, null);

        field.IsSuccess.ShouldBeTrue();

        draft.Submit(MeasurementTestData.Now, null).IsSuccess.ShouldBeTrue();

        // Submitted is already too late to edit: a reviewer reads a version that cannot change under them.
        draft.AddField(
                MeasurementTestData.Id("late"), MeasurementTestData.Field("waist"), MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.version-not-editable");

        draft.Approve(MeasurementTestData.Now, null, soleAdministrator: true).IsSuccess.ShouldBeTrue();
        template.PublishVersion(draft.Id, MeasurementTestData.Now, null, "Approved at review")
            .IsSuccess.ShouldBeTrue();

        draft.Status.ShouldBe(TemplateStatus.Published);
        draft.EditField(field.Value.Id, MeasurementTestData.Field("chest_bust"), MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.version-not-editable");
        draft.RemoveField(field.Value.Id, MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.version-not-editable");
    }

    [Fact]
    public void TheSubmitterDoesNotApproveTheirOwnVersion()
    {
        var (_, draft) = MeasurementTestData.WithDraft();
        var submitter = MeasurementTestData.Id("submitter");

        draft.AddField(
            MeasurementTestData.Id("field"), MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Now, submitter);
        draft.Submit(MeasurementTestData.Now, submitter);

        draft.Approve(MeasurementTestData.Now, submitter, soleAdministrator: false)
            .Error.Code.ShouldBe("measurements.submitter-cannot-publish");

        // A shop with one administrator has nobody else, and refusing there would mean it could never publish a
        // template at all.
        draft.Approve(MeasurementTestData.Now, submitter, soleAdministrator: true).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ReturningAVersionToDraftDropsItsApproval()
    {
        // The defect this guards: if the approval survived, a version could come back for changes, be edited, be
        // resubmitted and be published on the strength of somebody having read an earlier draft.
        var (template, draft) = MeasurementTestData.WithDraft();
        var submitter = MeasurementTestData.Id("submitter");
        var reviewer = MeasurementTestData.Id("reviewer");

        draft.AddField(
            MeasurementTestData.Id("field"), MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Now, submitter);
        draft.Submit(MeasurementTestData.Now, submitter);
        draft.Approve(MeasurementTestData.Now, reviewer, soleAdministrator: false).IsSuccess.ShouldBeTrue();

        draft.ReturnToDraft(MeasurementTestData.Now, reviewer).IsSuccess.ShouldBeTrue();
        draft.ApprovedAt.ShouldBeNull();
        draft.ApprovedBy.ShouldBeNull();

        draft.Submit(MeasurementTestData.Now, submitter).IsSuccess.ShouldBeTrue();
        template.PublishVersion(draft.Id, MeasurementTestData.Now, reviewer, "Trying it on")
            .Error.Code.ShouldBe("measurements.version-not-publishable");
    }

    [Fact]
    public void AVersionIsApprovedOnce()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
            MeasurementTestData.Id("field"), MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Now, null);
        draft.Submit(MeasurementTestData.Now, null);
        draft.Approve(MeasurementTestData.Now, MeasurementTestData.Id("first"), soleAdministrator: false)
            .IsSuccess.ShouldBeTrue();

        draft.Approve(MeasurementTestData.Now, MeasurementTestData.Id("second"), soleAdministrator: false)
            .Error.Code.ShouldBe("measurements.version-already-approved");
        draft.ApprovedBy.ShouldBe(MeasurementTestData.Id("first"), "the first approval is the record");
    }

    [Fact]
    public void PublishingRetiresTheVersionItSupersedes()
    {
        var (template, first) = MeasurementTestData.WithDraft();

        first.AddField(
            MeasurementTestData.Id("f1"), MeasurementTestData.Field("chest_bust"), MeasurementTestData.Now, null);
        first.Submit(MeasurementTestData.Now, null);
        first.Approve(MeasurementTestData.Now, null, soleAdministrator: true);
        template.PublishVersion(first.Id, MeasurementTestData.Now, null, "First").Value.ShouldBeNull();

        var second = template.StartDraft(
            new MeasurementTestData.CountingIds("v2"), "Version 2", null, DisplayUnit.Inch, first.Id,
            MeasurementTestData.Now, null).Value;

        second.VersionNumber.ShouldBe(2);
        second.Fields.Count.ShouldBe(1, "cloning copies the field set");
        second.Fields[0].Key.Value.ShouldBe("chest_bust", "a clone keeps the keys captured values are filed under");
        second.Fields[0].Id.ShouldNotBe(first.Fields[0].Id, "with fresh row identities");

        second.Submit(MeasurementTestData.Now, null);
        second.Approve(MeasurementTestData.Now, null, soleAdministrator: true);

        var superseded = template.PublishVersion(second.Id, MeasurementTestData.Now, null, "Widened the band");

        superseded.Value!.Id.ShouldBe(first.Id);
        first.Status.ShouldBe(TemplateStatus.Retired);
        first.RetiredReason.ShouldNotBeNull().ShouldContain("Superseded by version 2");
        template.PublishedVersion!.Id.ShouldBe(second.Id);
    }

    [Fact]
    public void AVersionNeverGoesBackwardsOnceItIsPublished()
    {
        var (template, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
            MeasurementTestData.Id("f"), MeasurementTestData.Field("chest_bust"), MeasurementTestData.Now, null);
        draft.Submit(MeasurementTestData.Now, null);
        draft.Approve(MeasurementTestData.Now, null, soleAdministrator: true);
        template.PublishVersion(draft.Id, MeasurementTestData.Now, null, "Live");

        draft.ReturnToDraft(MeasurementTestData.Now, null).Error.Code
            .ShouldBe("measurements.version-not-approvable");
        draft.Submit(MeasurementTestData.Now, null).Error.Code.ShouldBe("measurements.version-not-submittable");

        draft.Retire(MeasurementTestData.Now, null, "Withdrawn").IsSuccess.ShouldBeTrue();
        draft.Retire(MeasurementTestData.Now, null, "Again").Error.Code
            .ShouldBe("measurements.version-not-retirable");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PublishingAndRetiringBothDemandAReason(string reason)
    {
        var (template, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
            MeasurementTestData.Id("f"), MeasurementTestData.Field("chest_bust"), MeasurementTestData.Now, null);
        draft.Submit(MeasurementTestData.Now, null);
        draft.Approve(MeasurementTestData.Now, null, soleAdministrator: true);

        template.PublishVersion(draft.Id, MeasurementTestData.Now, null, reason)
            .Error.Code.ShouldBe("measurements.value-required");
        draft.Status.ShouldBe(TemplateStatus.InReview, "a refused publication changes nothing");

        template.PublishVersion(draft.Id, MeasurementTestData.Now, null, "Live").IsSuccess.ShouldBeTrue();
        draft.Retire(MeasurementTestData.Now, null, reason).Error.Code.ShouldBe("measurements.value-required");
    }

    [Fact]
    public void AnEmptyVersionIsNotSubmitted()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.Submit(MeasurementTestData.Now, null).Error.Code.ShouldBe("measurements.version-has-no-fields");
    }

    [Fact]
    public void AFieldKeepsItsKeyThroughAnEdit()
    {
        var (_, draft) = MeasurementTestData.WithDraft();
        var field = draft.AddField(
            MeasurementTestData.Id("f"), MeasurementTestData.Field("chest_bust"),
            MeasurementTestData.Now, null).Value;

        // Renaming through an edit would orphan every value already filed under the old key, so the key is taken
        // from the field rather than from the request.
        var edited = draft.EditField(
            field.Id,
            MeasurementTestData.Field("something_else") with { Label = "Chest (bust)" },
            MeasurementTestData.Now,
            null);

        edited.IsSuccess.ShouldBeTrue();
        edited.Value.Key.Value.ShouldBe("chest_bust");
        edited.Value.Label.ShouldBe("Chest (bust)");
    }

    [Fact]
    public void TwoFieldsCannotClaimTheSameKey()
    {
        var (_, draft) = MeasurementTestData.WithDraft();

        draft.AddField(
            MeasurementTestData.Id("a"), MeasurementTestData.Field("chest_bust"), MeasurementTestData.Now, null);

        draft.AddField(
                MeasurementTestData.Id("b"), MeasurementTestData.Field("chest_bust"), MeasurementTestData.Now, null)
            .Error.Code.ShouldBe("measurements.duplicate-field-key");
    }
}
