using CoffeePeek.Contract.Dtos.Coffee;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed record SetRoasterTagsCommand(Guid RoasterId, Guid[] TagIds, Guid ActorId);
