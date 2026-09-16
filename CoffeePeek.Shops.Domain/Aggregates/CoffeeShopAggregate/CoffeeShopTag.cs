using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public sealed class CoffeeShopTag
{
    public Guid ShopId { get; private set; }
    public Guid TagId { get; private set; }
    public Guid? AssignedByUserId { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }

    public ShopTag? Tag { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private CoffeeShopTag()
    {
    }

    public CoffeeShopTag(Guid shopId, Guid tagId, Guid? assignedByUserId, DateTime assignedAtUtc)
    {
        ShopId = shopId;
        TagId = tagId;
        AssignedByUserId = assignedByUserId;
        AssignedAtUtc = assignedAtUtc;
    }
}
