namespace CoffeePeek.Shops.Application.Features.Shelf;

/// <summary>Read-only CoffeeCatalog projection used when a bag selects a product.</summary>
public sealed record ShelfCatalogProduct(Guid Id, string Name, string? RoasterName);
