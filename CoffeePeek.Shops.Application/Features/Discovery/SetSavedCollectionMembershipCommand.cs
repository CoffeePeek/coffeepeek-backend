using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record SetSavedCollectionMembershipCommand(Guid OwnerId, string Kind, string Slug, Guid[] CollectionIds);
