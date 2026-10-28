namespace CoffeePeek.ShopsService.Controllers.Admin;

/// <summary>Full permutation of gallery photo IDs in desired display order (index 0 = cover).</summary>
public record ReorderCoffeeShopPhotosRequest(IReadOnlyList<Guid> PhotoIds);
