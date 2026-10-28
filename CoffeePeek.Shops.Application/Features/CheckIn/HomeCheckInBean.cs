namespace CoffeePeek.Shops.Application.Features.CheckIn;

/// <summary>The resolved bean of a home check-in and where it came from.</summary>
internal sealed record HomeCheckInBean(Guid? CoffeeProductId, string? BeanName, string? RoasterName, Guid? ShelfBagId);
