using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public sealed class AppliedShopChangeRequest : Entity<Guid>
{
    public Guid ShopId { get; private set; }
    public Guid ApprovedByUserId { get; private set; }
    public DateTime AppliedAtUtc { get; private set; }

    private AppliedShopChangeRequest() { }

    public static AppliedShopChangeRequest Create(
        Guid requestId,
        Guid shopId,
        Guid approvedByUserId,
        DateTime appliedAtUtc) => new()
    {
        Id = requestId,
        ShopId = shopId,
        ApprovedByUserId = approvedByUserId,
        AppliedAtUtc = appliedAtUtc,
        CreatedAtUtc = appliedAtUtc
    };
}
