using Shouldly;
using Tailor360.Modules.Billing.Application;
using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Application.Tax;
using Tailor360.Modules.Billing.Domain.Tax;

namespace Tailor360.UnitTests.Billing;

/// <summary>The publication checks (#145): each one with a minimal failing version, and a clean version they pass.</summary>
[Trait("Category", "Unit")]
public sealed class TaxConfigurationPublicationCheckTests
{
    [Fact]
    public void FindsNothingWrongWithAWellFormedVersion()
    {
        var version = BillingTestData.Draft();
        Add(version, BillingTestData.ServiceCode());
        Add(version, BillingTestData.ServiceCode("GOODS_12", half: 6m, classification: "5208") with { Kind = TaxCodeKind.Goods });

        var report = TaxConfigurationPublicationCheck.Run(version, TaxCodeLedger.Empty, null);

        report.HasErrors.ShouldBeFalse(string.Join("; ", report.Findings.Select(finding => finding.Message)));
        report.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void RefusesAHalfPairUnequalHalvesAMissingInterStateRateAndOneThatIsNotTheSum()
    {
        var version = BillingTestData.Draft();
        Add(version, BillingTestData.ServiceCode("HALF") with { Rates = [new TaxRate(TaxComponentKind.Cgst, 2.5m)] });
        Add(version, BillingTestData.ServiceCode("UNEQUAL") with
        {
            Rates = [new TaxRate(TaxComponentKind.Cgst, 2.5m), new TaxRate(TaxComponentKind.Sgst, 3m), new TaxRate(TaxComponentKind.Igst, 5.5m)],
        });
        Add(version, BillingTestData.ServiceCode("NO_IGST") with
        {
            Rates = [new TaxRate(TaxComponentKind.Cgst, 2.5m), new TaxRate(TaxComponentKind.Sgst, 2.5m)],
        });
        Add(version, BillingTestData.ServiceCode("NOT_SUM") with
        {
            Rates = [new TaxRate(TaxComponentKind.Cgst, 2.5m), new TaxRate(TaxComponentKind.Sgst, 2.5m), new TaxRate(TaxComponentKind.Igst, 6m)],
        });
        Add(version, BillingTestData.ServiceCode("ONLY_IGST") with { Rates = [new TaxRate(TaxComponentKind.Igst, 5m)] });

        var report = TaxConfigurationPublicationCheck.Run(version, TaxCodeLedger.Empty, null);

        report.HasErrors.ShouldBeTrue();
        var errors = report.Findings.Where(finding => finding.Severity == BillingFindingSeverity.Error)
            .Select(finding => (finding.Code, finding.Target)).ToArray();
        errors.ShouldContain(("billing.intra-state-pair-incomplete", "taxCodes[HALF].rates"));
        errors.ShouldContain(("billing.inter-state-rate-missing", "taxCodes[HALF].rates"));
        errors.ShouldContain(("billing.intra-state-pair-unequal", "taxCodes[UNEQUAL].rates"));
        errors.ShouldContain(("billing.inter-state-rate-missing", "taxCodes[NO_IGST].rates"));
        errors.ShouldContain(("billing.inter-state-rate-not-sum", "taxCodes[NOT_SUM].rates"));
        errors.ShouldContain(("billing.intra-state-pair-missing", "taxCodes[ONLY_IGST].rates"));
        errors.Length.ShouldBe(6);
    }

    [Fact]
    public void WarnsAboutANilRatedCodeAndAnEmptyVersionWithoutRefusingEither()
    {
        var empty = TaxConfigurationPublicationCheck.Run(BillingTestData.Draft(), TaxCodeLedger.Empty, null);
        empty.HasErrors.ShouldBeFalse();
        empty.Findings.Single().Code.ShouldBe("billing.no-tax-codes");

        var version = BillingTestData.Draft();
        Add(version, BillingTestData.NilRated());
        var nil = TaxConfigurationPublicationCheck.Run(version, TaxCodeLedger.Empty, null);
        nil.HasErrors.ShouldBeFalse();
        nil.Findings.Single().Code.ShouldBe("billing.nil-rated-code");

        // A cess with no GST component beside it is most likely a code entered halfway: a warning.
        var cessOnly = BillingTestData.Draft(2);
        Add(cessOnly, BillingTestData.ServiceCode("CESS_ONLY") with { Rates = [new TaxRate(TaxComponentKind.Cess, 1m)] });
        var halfway = TaxConfigurationPublicationCheck.Run(cessOnly, TaxCodeLedger.Empty, null);
        halfway.HasErrors.ShouldBeFalse();
        halfway.Findings.Single().Code.ShouldBe("billing.cess-only-code");
    }

    [Fact]
    public void RefusesAPublishedCodeRespelledAndACodeReusedForAnotherConcept()
    {
        var version = BillingTestData.Draft();
        var respelled = Add(version, BillingTestData.ServiceCode("STITCHING_NEW"));
        var reused = Add(version, BillingTestData.NilRated("STITCHING_5"));
        var ledger = new TaxCodeLedger(
            new Dictionary<Guid, string> { [respelled.Key] = "STITCHING_5" },
            new Dictionary<string, Guid>(StringComparer.Ordinal) { ["STITCHING_5"] = respelled.Key });

        var report = TaxConfigurationPublicationCheck.Run(version, ledger, null);

        var errors = report.Findings.Where(finding => finding.Severity == BillingFindingSeverity.Error).ToArray();
        errors.Select(finding => (finding.Code, finding.Target)).ShouldBe(
        [
            ("billing.code-reused", $"taxCodes[{reused.Code}].code"),
            ("billing.published-code-changed", $"taxCodes[{respelled.Code}].code"),
        ]);
    }

    [Fact]
    public void RefusesAnEffectiveDateBeforeThePublishedVersions()
    {
        var published = BillingTestData.Draft(1, new DateOnly(2026, 4, 1));
        Add(published, BillingTestData.ServiceCode());
        published.Publish(BillingTestData.Now, null, "First.").IsSuccess.ShouldBeTrue();

        var earlier = BillingTestData.Draft(2, new DateOnly(2026, 3, 1));
        Add(earlier, BillingTestData.ServiceCode());

        var report = TaxConfigurationPublicationCheck.Run(earlier, TaxCodeLedger.Empty, published);

        report.Findings.Single().Code.ShouldBe("billing.effective-from-before-published");
        report.Findings.Single().Target.ShouldBe("effectiveFrom");

        var later = BillingTestData.Draft(3, new DateOnly(2026, 4, 1));
        Add(later, BillingTestData.ServiceCode());
        TaxConfigurationPublicationCheck.Run(later, TaxCodeLedger.Empty, published).HasErrors.ShouldBeFalse();
    }

    private static TaxCode Add(TaxConfigurationVersion version, TaxCodeDetails details)
    {
        var added = version.AddTaxCode(
            BillingTestData.Id($"code-{details.Code}"), BillingTestData.Id($"key-{details.Code}"), details, BillingTestData.Now, null);
        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Message : string.Empty);
        return added.Value;
    }
}
