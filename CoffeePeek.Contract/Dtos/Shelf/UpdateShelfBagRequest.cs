#nullable enable

using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Shelf;

/// <summary>Replaces the bag's details; omitted values are cleared. An omitted Photo keeps the current card.</summary>
public sealed record UpdateShelfBagRequest
{
    /// <summary>A new upload replaces the card; null keeps it.</summary>
    public UploadedPhotoDto? Photo { get; init; }
    /// <summary>Keep this bag's existing catalog product and its name snapshot.</summary>
    public Guid? CoffeeProductId { get; init; }
    public string? CoffeeProductSlug { get; init; }
    public string? CustomName { get; init; }
    public string? RoasterName { get; init; }
    public int? WeightGrams { get; init; }
    public ShelfBagGrind? Grind { get; init; }
    public ShelfBagRoastPurpose? RoastPurpose { get; init; }
    public DateOnly? RoastedOn { get; init; }
}
