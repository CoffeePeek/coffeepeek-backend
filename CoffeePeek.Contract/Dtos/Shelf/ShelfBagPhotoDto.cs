namespace CoffeePeek.Contract.Dtos.Shelf;

/// <param name="Url">Owner-only Shops URL; changes whenever the card is replaced.</param>
public sealed record ShelfBagPhotoDto(Guid Id, string Url);
