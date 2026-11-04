namespace CoffeePeek.Contract.Dtos.Shelf;

public sealed record PublicShelfDto(PublicShelfItemDto[] Items, int TotalCount);
