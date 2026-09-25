using Shouldly;
using Tailor360.Modules.Orders.Domain.Drafts;

namespace Tailor360.UnitTests.Orders;

[Trait("Category", "Unit")]
public sealed class OrderDraftTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DraftPinsCustomerAndCatalogueSelectionWithoutCreatingAnOrder()
    {
        var draft = Start();
        var result = draft.AddGarment(
            Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca116"),
            Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca117"),
            Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca118"),
            "BLOUSE", "STITCH", "Blouse stitching", 2, "Sleeves supplied",
            StartedAt.AddMinutes(1), Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca119"));

        result.IsSuccess.ShouldBeTrue();
        draft.CustomerNumber.ShouldBe("C-BR-000001");
        draft.ExpiresAt.ShouldBe(StartedAt.AddHours(72));
        draft.Garments.Single().ServiceName.ShouldBe("Blouse stitching");
        draft.Garments.Single().Quantity.ShouldBe(2);
    }

    [Fact]
    public void InvalidOrExpiredGarmentsNeverChangeTheDraft()
    {
        var draft = Start();
        var garmentId = Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca116");
        var serviceId = Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca117");
        var versionId = Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca118");
        var actorId = Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca119");

        draft.AddGarment(garmentId, serviceId, versionId, "BLOUSE", "STITCH", "Blouse", 0,
            null, StartedAt.AddMinutes(1), actorId).Error.Code.ShouldBe("orders.draft.invalid-quantity");
        draft.AddGarment(garmentId, serviceId, versionId, "BLOUSE", "STITCH", "Blouse", 1,
            new string('x', 1001), StartedAt.AddMinutes(1), actorId).Error.Code
            .ShouldBe("orders.draft.notes-too-long");
        draft.AddGarment(garmentId, serviceId, versionId, "BLOUSE", "STITCH", "Blouse", 1,
            null, StartedAt.AddHours(72), actorId).Error.Code.ShouldBe("orders.draft.expired");

        draft.Garments.ShouldBeEmpty();
        draft.UpdatedAt.ShouldBe(StartedAt);
    }

    private static OrderDraft Start()
        => OrderDraft.Start(
            Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca111"),
            Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca112"),
            Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca113"),
            Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca114"),
            "C-BR-000001", "Sample Customer", StartedAt,
            Guid.Parse("0195f9cf-9650-7a08-845f-af76118ca115")).Value;
}
