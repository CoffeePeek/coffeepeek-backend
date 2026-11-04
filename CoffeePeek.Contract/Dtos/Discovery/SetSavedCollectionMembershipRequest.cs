namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed record SetSavedCollectionMembershipRequest(Guid[] CollectionIds,
    Guid[]? AddedCollectionIds = null, Guid[]? RemovedCollectionIds = null);
