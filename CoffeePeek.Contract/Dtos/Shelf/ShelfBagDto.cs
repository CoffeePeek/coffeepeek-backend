#nullable enable

using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Shelf;

/// <summary>One coffee bag on the owner's private shelf or in its archive.</summary>
public sealed record ShelfBagDto
{
    public Guid Id { get; init; }
    public ShelfBagStatus Status { get; init; }
    /// <summary>Zero-based shelf index for OnShelf; null for Finished.</summary>
    public int? Position { get; init; }
    public ShelfBagPhotoDto? Photo { get; init; }
    public Guid? CoffeeProductId { get; init; }
    /// <summary>Currently published slug of CoffeeProductId, or null when it is no longer published.</summary>
    public string? CoffeeProductSlug { get; init; }
    /// <summary>Name snapshot taken when the product was selected, or the custom name.</summary>
    public string? Name { get; init; }
    public string? RoasterName { get; init; }
    public int? WeightGrams { get; init; }
    public ShelfBagGrind? Grind { get; init; }
    public ShelfBagRoastPurpose? RoastPurpose { get; init; }
    public DateOnly? RoastedOn { get; init; }
    public DateTime AddedAtUtc { get; init; }
    public DateTime? FinishedAtUtc { get; init; }
    public int? Rating { get; init; }
    public bool? BuyAgain { get; init; }
}
