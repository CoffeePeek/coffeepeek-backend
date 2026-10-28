namespace CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

/// <summary>Owner-editable description of a bag. Name is the catalog snapshot or a custom name.</summary>
public sealed record ShelfBagDetails(Guid? CoffeeProductId, string? Name, string? RoasterName, int? WeightGrams,
    ShelfBagGrind? Grind, ShelfBagRoastPurpose? RoastPurpose, DateOnly? RoastedOn);
