using CoffeePeek.Shared.Domain.PublicAddresses;

namespace CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;

public sealed class CoffeeBeanPublicAddress : PublicAddress
{
    private CoffeeBeanPublicAddress() { }
    private CoffeeBeanPublicAddress(Guid ownerId, string slug, long revision, DateTime now) : base(ownerId, slug, revision, now) { }
    public static CoffeeBeanPublicAddress Create(Guid ownerId, string slug, long revision, DateTime now) => new(ownerId, slug, revision, now);
}

public sealed class EquipmentPublicAddress : PublicAddress
{
    private EquipmentPublicAddress() { }
    private EquipmentPublicAddress(Guid ownerId, string slug, long revision, DateTime now) : base(ownerId, slug, revision, now) { }
    public static EquipmentPublicAddress Create(Guid ownerId, string slug, long revision, DateTime now) => new(ownerId, slug, revision, now);
}

public sealed class BrewMethodPublicAddress : PublicAddress
{
    private BrewMethodPublicAddress() { }
    private BrewMethodPublicAddress(Guid ownerId, string slug, long revision, DateTime now) : base(ownerId, slug, revision, now) { }
    public static BrewMethodPublicAddress Create(Guid ownerId, string slug, long revision, DateTime now) => new(ownerId, slug, revision, now);
}

public sealed class ShopTagPublicAddress : PublicAddress
{
    private ShopTagPublicAddress() { }
    private ShopTagPublicAddress(Guid ownerId, string slug, long revision, DateTime now) : base(ownerId, slug, revision, now) { }
    public static ShopTagPublicAddress Create(Guid ownerId, string slug, long revision, DateTime now) => new(ownerId, slug, revision, now);
}

