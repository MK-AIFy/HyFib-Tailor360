using Shouldly;
using Tailor360.Modules.Orders.Application.Options;
using Tailor360.Modules.Orders.Domain.Drafts;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The module-level binding of the draft window, used until a branch-configuration surface exists (#199).
/// </summary>
[Trait("Category", "Unit")]
public sealed class OrdersDraftOptionsTests
{
    [Fact]
    public void TheUnboundDefaultIsTheDomainsDocumentedSeventyTwoHours()
    {
        // Bound from OrderDraft.DefaultLifetime rather than restated as a second literal, so a copy-paste
        // of Catalog's 24 hours or Customers' 48 would be caught here rather than discovered at a branch.
        new OrdersDraftOptions().DraftLifetime.ShouldBe(OrderDraft.DefaultLifetime);
        new OrdersDraftOptions().DraftLifetime.ShouldBe(TimeSpan.FromHours(72));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(30 * 24)]
    public void ALifetimeBetweenOneHourAndThirtyDaysIsUsable(double hours)
        => new OrdersDraftOptions { DraftLifetime = TimeSpan.FromHours(hours) }.IsLifetimeUsable.ShouldBeTrue();

    [Theory]
    [InlineData(0.5)]
    [InlineData(30 * 24 + 1)]
    public void ALifetimeOutsideOneHourToThirtyDaysIsNotUsable(double hours)
        => new OrdersDraftOptions { DraftLifetime = TimeSpan.FromHours(hours) }.IsLifetimeUsable.ShouldBeFalse();
}
