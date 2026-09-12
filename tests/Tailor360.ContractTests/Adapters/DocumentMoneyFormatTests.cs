using Shouldly;
using Tailor360.Modules.Integration.Infrastructure.Documents;

namespace Tailor360.ContractTests.Adapters;

/// <summary>The amount format a document prints: two decimals, lakh and crore grouping, the rupee sign.</summary>
[Trait("Category", "Contract")]
public sealed class DocumentMoneyFormatTests
{
    [Theory]
    [InlineData(0, "₹0.00")]
    [InlineData(567, "₹567.00")]
    [InlineData(1134, "₹1,134.00")]
    [InlineData(113400.5, "₹1,13,400.50")]
    [InlineData(12345678.9, "₹1,23,45,678.90")]
    [InlineData(-94.5, "-₹94.50")]
    public void GroupsByLakhAndCrore(double amount, string expected)
        => DocumentModel.Money((decimal)amount, "INR").ShouldBe(expected);
}
