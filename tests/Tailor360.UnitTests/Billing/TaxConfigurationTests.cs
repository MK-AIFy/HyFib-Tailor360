using Shouldly;
using Tailor360.Modules.Billing.Domain.Tax;
using Tailor360.Platform.Abstractions.Identifiers;

namespace Tailor360.UnitTests.Billing;

/// <summary>
/// The tax configuration version aggregate (#145): what a draft accepts, what a published version
/// refuses, and what a clone carries.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TaxConfigurationTests
{
    [Fact]
    public void AddsEditsAndRemovesACodeOnADraft()
    {
        var version = BillingTestData.Draft();

        var added = version.AddTaxCode(BillingTestData.Id("c1"), BillingTestData.Id("k1"), BillingTestData.ServiceCode(), BillingTestData.Now, null);
        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Message : string.Empty);
        added.Value.Rates.Select(rate => (rate.Kind, rate.RatePercent))
            .ShouldBe([(TaxComponentKind.Cgst, 2.5m), (TaxComponentKind.Sgst, 2.5m), (TaxComponentKind.Igst, 5m)]);
        added.Value.RateOf(TaxComponentKind.Cess).ShouldBe(0m);

        var edited = version.EditTaxCode(added.Value.Id, BillingTestData.ServiceCode(half: 6m), BillingTestData.Now, null);
        edited.IsSuccess.ShouldBeTrue();
        edited.Value.RateOf(TaxComponentKind.Igst).ShouldBe(12m);

        version.RemoveTaxCode(added.Value.Id, BillingTestData.Now, null).IsSuccess.ShouldBeTrue();
        version.TaxCodes.ShouldBeEmpty();
        version.RemoveTaxCode(added.Value.Id, BillingTestData.Now, null).Error.Code.ShouldBe("billing.tax-code-not-found");
    }

    [Theory]
    [InlineData("stitching", "billing.code-not-well-formed", "code")]
    [InlineData("S", "billing.code-not-well-formed", "code")]
    public void RefusesAMalformedCode(string code, string expectedCode, string target)
    {
        var refused = BillingTestData.Draft().AddTaxCode(
            BillingTestData.Id("c"), BillingTestData.Id("k"), BillingTestData.ServiceCode(code), BillingTestData.Now, null);

        refused.Error.Code.ShouldBe(expectedCode);
        refused.Error.Target.ShouldBe(target);
    }

    [Fact]
    public void RefusesADuplicateCodeAMalformedClassificationADuplicateComponentAndARateOutOfRange()
    {
        var version = BillingTestData.Draft();
        version.AddTaxCode(BillingTestData.Id("c1"), BillingTestData.Id("k1"), BillingTestData.ServiceCode(), BillingTestData.Now, null)
            .IsSuccess.ShouldBeTrue();

        Add(version, BillingTestData.ServiceCode()).Error.Code.ShouldBe("billing.code-not-unique");
        Add(version, BillingTestData.ServiceCode("OTHER", classification: "99A")).Error.Target.ShouldBe("classification");

        var duplicated = Add(version, BillingTestData.ServiceCode("OTHER") with
        {
            Rates = [new TaxRate(TaxComponentKind.Cgst, 2.5m), new TaxRate(TaxComponentKind.Cgst, 2.5m)],
        });
        duplicated.Error.Code.ShouldBe("billing.component-duplicated");
        duplicated.Error.Target.ShouldBe("rates[1].kind");

        Add(version, BillingTestData.ServiceCode("OTHER") with { Rates = [new TaxRate(TaxComponentKind.Cess, 100.001m)] })
            .Error.Code.ShouldBe("billing.rate-out-of-range");
        Add(version, BillingTestData.ServiceCode("OTHER") with { Rates = [new TaxRate(TaxComponentKind.Cess, 1.2345m)] })
            .Error.Code.ShouldBe("billing.rate-out-of-range");
        Add(version, BillingTestData.ServiceCode("OTHER") with { Rates = [new TaxRate((TaxComponentKind)9, 1m)] })
            .Error.Code.ShouldBe("billing.component-not-well-formed");
        Add(version, BillingTestData.ServiceCode("OTHER") with { Description = " " }).Error.Target.ShouldBe("description");
    }

    [Fact]
    public void ANilRatedCodeIsAccepted()
    {
        var added = Add(BillingTestData.Draft(), BillingTestData.NilRated());

        added.IsSuccess.ShouldBeTrue();
        added.Value.Rates.ShouldBeEmpty();
    }

    [Fact]
    public void RefusesEveryChangeOncePublishedAndRetiresOnlyFromPublished()
    {
        var version = BillingTestData.Draft();
        var code = Add(version, BillingTestData.ServiceCode()).Value;

        version.Publish(BillingTestData.Now, null, " ").Error.Code.ShouldBe("billing.reason-required");
        version.Retire(BillingTestData.Now, null, "Too soon.").Error.Code.ShouldBe("billing.version-not-retirable");
        version.Publish(BillingTestData.Now, null, "Rates for the new year.").IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(TaxConfigurationStatus.Published);
        version.IsEditable.ShouldBeFalse();

        Add(version, BillingTestData.NilRated()).Error.Code.ShouldBe("billing.version-not-editable");
        version.EditTaxCode(code.Id, BillingTestData.ServiceCode(half: 9m), BillingTestData.Now, null).Error.Code.ShouldBe("billing.version-not-editable");
        version.RemoveTaxCode(code.Id, BillingTestData.Now, null).Error.Code.ShouldBe("billing.version-not-editable");
        version.Describe("Renamed", null, BillingTestData.Today, BillingTestData.Now, null).Error.Code.ShouldBe("billing.version-not-editable");
        version.Publish(BillingTestData.Now, null, "Again.").Error.Code.ShouldBe("billing.version-not-publishable");

        version.Retire(BillingTestData.Now, null, "Superseded.").IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(TaxConfigurationStatus.Retired);
        version.RetiredReason.ShouldBe("Superseded.");
    }

    [Fact]
    public void ACloneCarriesTheCodesWithTheSameKeysAndFreshRows()
    {
        var version = BillingTestData.Draft();
        var stitching = Add(version, BillingTestData.ServiceCode()).Value;
        var exempt = Add(version, BillingTestData.NilRated()).Value;
        version.Publish(BillingTestData.Now, null, "First.").IsSuccess.ShouldBeTrue();

        var cloned = version.CloneAsDraft(new SequentialIds(), 2, "Version 2", "Cloned.", BillingTestData.Today.AddMonths(1), BillingTestData.Now, null);

        cloned.IsSuccess.ShouldBeTrue();
        var clone = cloned.Value;
        clone.Status.ShouldBe(TaxConfigurationStatus.Draft);
        clone.ClonedFromVersionId.ShouldBe(version.Id);
        clone.VersionNumber.ShouldBe(2);
        clone.TaxCodes.Select(code => code.Key).ShouldBe([stitching.Key, exempt.Key], ignoreOrder: true);
        clone.TaxCodes.Select(code => code.Id).ShouldNotContain(stitching.Id);
        clone.TaxCodes.ShouldAllBe(code => code.TaxConfigurationVersionId == clone.Id);
        clone.FindTaxCodeByCode("STITCHING_5")!.Rates.ShouldBe(stitching.Rates);
    }

    [Fact]
    public void RefusesAnOmittedEffectiveDate()
    {
        // The first day of year one is what an omitted date binds as; nothing is in force from then.
        TaxConfigurationVersion.CreateDraft(BillingTestData.Id("v"), BillingTestData.Organisation, 1, "Version", null, default, BillingTestData.Now, null)
            .Error.Target.ShouldBe("effectiveFrom");
        var version = BillingTestData.Draft();
        version.Describe("Version", null, default, BillingTestData.Now, null).Error.Target.ShouldBe("effectiveFrom");
        version.CloneAsDraft(new SequentialIds(), 2, "Clone", null, default, BillingTestData.Now, null).Error.Target.ShouldBe("effectiveFrom");
    }

    [Fact]
    public void RefusesANameTooLongAndAnEmptyName()
    {
        TaxConfigurationVersion.CreateDraft(BillingTestData.Id("v"), BillingTestData.Organisation, 1, " ", null, BillingTestData.Today, BillingTestData.Now, null)
            .Error.Target.ShouldBe("name");
        TaxConfigurationVersion.CreateDraft(BillingTestData.Id("v"), BillingTestData.Organisation, 1, new string('x', 121), null, BillingTestData.Today, BillingTestData.Now, null)
            .Error.Code.ShouldBe("billing.value-too-long");
    }

    private static Tailor360.Platform.Abstractions.Results.Result<TaxCode> Add(TaxConfigurationVersion version, TaxCodeDetails details)
        => version.AddTaxCode(
            BillingTestData.Id($"code-{details.Code}-{details.GetHashCode()}"),
            BillingTestData.Id($"key-{details.Code}-{details.GetHashCode()}"),
            details,
            BillingTestData.Now,
            null);

    private sealed class SequentialIds : IIdGenerator
    {
        private int _next;

        public Guid NewId() => BillingTestData.Id($"sequential-{++_next}");
    }
}
