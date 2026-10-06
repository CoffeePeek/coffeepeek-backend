using CoffeePeek.Contract.Dtos.Coffee;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed record EditRoasterTagCommand(Guid Id, RoasterTagEdit Request);
