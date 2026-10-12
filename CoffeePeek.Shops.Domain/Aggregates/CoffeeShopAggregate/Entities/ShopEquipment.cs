namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

/// <summary>Equipment as used by one shop; primary status is local to this assignment.</summary>
public sealed class ShopEquipment
{
    public Guid ShopId { get; private set; }
    public Guid EquipmentId { get; private set; }
    public int CategoryId { get; private set; }
    public bool IsPrimary { get; private set; }
    public Equipment Equipment { get; private set; } = null!;

    private ShopEquipment() { }

    internal ShopEquipment(Guid shopId, Equipment equipment, bool isPrimary = false)
    {
        ShopId = shopId;
        EquipmentId = equipment.Id;
        CategoryId = equipment.CategoryId;
        Equipment = equipment;
        IsPrimary = isPrimary;
    }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}
