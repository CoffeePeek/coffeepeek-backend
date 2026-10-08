namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>A shop with an associated check-in count.</summary>
public record AdminTopShopDto(Guid ShopId, string Name, int Count);
