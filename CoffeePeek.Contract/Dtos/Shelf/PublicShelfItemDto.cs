namespace CoffeePeek.Contract.Dtos.Shelf;

/// <summary>Only the current coffee card, with no owner identity, archive or private metadata.</summary>
public sealed record PublicShelfItemDto(Guid Id, string Name, string? RoasterName, string? CoffeeSlug, string? PhotoUrl);
