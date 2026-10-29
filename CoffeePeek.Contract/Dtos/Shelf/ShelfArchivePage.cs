namespace CoffeePeek.Contract.Dtos.Shelf;

public sealed record ShelfArchivePage(ShelfBagDto[] Items, int TotalCount);
