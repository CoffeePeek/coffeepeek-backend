using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Shop;

public class BrewMethodDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public BrewMethodCategoryEnum Category { get; set; }
}