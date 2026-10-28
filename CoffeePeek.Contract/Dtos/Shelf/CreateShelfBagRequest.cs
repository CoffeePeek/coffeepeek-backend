#nullable enable

using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Shelf;

/// <summary>A new bag needs a card photo or a bean (CoffeeProductSlug or CustomName).</summary>
public sealed record CreateShelfBagRequest
{
    /// <summary>Upload from POST /api/Photos/shelf-bag; only StorageKey is trusted.</summary>
    public UploadedPhotoDto? Photo { get; init; }
    /// <summary>Currently published catalog product; mutually exclusive with CustomName.</summary>
    public string? CoffeeProductSlug { get; init; }
    public string? CustomName { get; init; }
    public string? RoasterName { get; init; }
    public int? WeightGrams { get; init; }
    public ShelfBagGrind? Grind { get; init; }
    public ShelfBagRoastPurpose? RoastPurpose { get; init; }
    public DateOnly? RoastedOn { get; init; }
}
