using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

public sealed class CoffeeZoneMembershipOverride
{
    public Guid ZoneId { get; private set; }
    public Guid ShopId { get; private set; }
    public CoffeeZoneMembershipOverrideKind Kind { get; private set; }

    private CoffeeZoneMembershipOverride()
    {
    }

    public CoffeeZoneMembershipOverride(Guid zoneId, Guid shopId, CoffeeZoneMembershipOverrideKind kind)
    {
        if (zoneId == Guid.Empty)
            throw new DomainException("ZoneId is required.");
        if (shopId == Guid.Empty)
            throw new DomainException("ShopId is required.");

        ValidateKind(kind);
        ZoneId = zoneId;
        ShopId = shopId;
        Kind = kind;
    }

    public void SetKind(CoffeeZoneMembershipOverrideKind kind)
    {
        ValidateKind(kind);
        Kind = kind;
    }

    private static void ValidateKind(CoffeeZoneMembershipOverrideKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new DomainException("Invalid coffee zone membership override kind.");
    }
}
