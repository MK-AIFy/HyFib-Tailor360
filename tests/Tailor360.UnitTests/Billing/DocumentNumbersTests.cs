using Shouldly;
using Tailor360.Modules.Billing.Domain.Invoicing;

namespace Tailor360.UnitTests.Billing;

/// <summary>The invoice, credit-note and debit-note numbers (#154): the shape, the branch code, and the financial-year boundary.</summary>
[Trait("Category", "Unit")]
public sealed class DocumentNumbersTests
{
    [Theory]
    [InlineData(2026, 4, 1, "2627")]
    [InlineData(2026, 9, 12, "2627")]
    [InlineData(2027, 3, 31, "2627")]
    [InlineData(2027, 4, 1, "2728")]
    [InlineData(2099, 4, 1, "9900")]
    public void TheFinancialYearTurnsOnTheFirstOfApril(int year, int month, int day, string token)
        => DocumentNumbers.FinancialYearToken(new DateOnly(year, month, day)).ShouldBe(token);

    [Fact]
    public void ComposesTheInterimFormatsWithASixDigitRunningNumber()
    {
        DocumentNumbers.Compose(DocumentNumbers.InvoicePrefix, "MAIN", "2627", 1).Value.ShouldBe("INV-MAIN-2627-000001");
        DocumentNumbers.Compose(DocumentNumbers.CreditNotePrefix, "main", "2627", 42).Value.ShouldBe("CN-MAIN-2627-000042");
        DocumentNumbers.Compose(DocumentNumbers.DebitNotePrefix, " br2 ", "2728", 1_000_000).Value.ShouldBe("DN-BR2-2728-1000000");
        DocumentNumbers.SequenceScope(BillingTestData.Organisation, "MAIN", "2627").ShouldBe($"{BillingTestData.Organisation:N}:MAIN-2627");
        DocumentNumbers.SequenceScope(BillingTestData.Organisation, new string('A', DocumentNumbers.MaximumBranchCodeLength), "2627").Length.ShouldBeLessThanOrEqualTo(64, "platform.sequences.scope is 64 wide");
    }

    [Fact]
    public void RefusesABranchCodeTheRegisterWouldNotWriteAndARunningNumberBelowOne()
    {
        DocumentNumbers.Compose(DocumentNumbers.InvoicePrefix, "", "2627", 1).Error.Code.ShouldBe("billing.value-required");
        DocumentNumbers.Compose(DocumentNumbers.InvoicePrefix, "MA-IN", "2627", 1).Error.Code.ShouldBe("billing.branch-code-not-well-formed");
        DocumentNumbers.Compose(DocumentNumbers.InvoicePrefix, new string('A', 17), "2627", 1).Error.Code.ShouldBe("billing.branch-code-not-well-formed");
        DocumentNumbers.Compose(DocumentNumbers.InvoicePrefix, "MAIN", "2627", 0).Error.Code.ShouldBe("billing.sequence-out-of-range");
    }
}
