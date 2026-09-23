using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public class Equipment : Entity<Guid>
{
    public string Name { get; private set; }
    public string Brand { get; private set; }
    public string ModelName { get; private set; }
    
    public bool IsCustom { get; private set; }
    public bool IsPrimary { get; private set; }
    
    public int CategoryId { get; private set; }
    public EquipmentCategory Category { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private Equipment() { }

    public Equipment(
        string brand, 
        string modelName, 
        EquipmentCategory category, 
        bool isCustom = false, 
        bool isPrimary = false)
    {
        if (string.IsNullOrWhiteSpace(brand)) throw new ArgumentException("Brand is required");
        if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("Model name is required");
        
        Id = Guid.NewGuid();

        Brand = brand;
        ModelName = modelName;
        Category = category;
        CategoryId = category.Id;
        IsCustom = isCustom;
        IsPrimary = isPrimary;
        
        Name = $"{Brand} {ModelName}";
    }

    public void Update(string brand, string modelName, EquipmentCategory category)
    {
        if (string.IsNullOrWhiteSpace(brand)) throw new ArgumentException("Brand is required");
        if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("Model name is required");

        Brand = brand;
        ModelName = modelName;
        Category = category;
        CategoryId = category.Id;

        Name = $"{Brand} {ModelName}";
    }

    public void MarkAsPrimary() => IsPrimary = true;
    public void UnmarkAsPrimary() => IsPrimary = false;
    
    public void SetCustom(bool custom) => IsCustom = custom;
}