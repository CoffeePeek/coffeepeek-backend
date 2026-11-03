namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed record ReplaceSavedCollectionItemsRequest(FavoriteCollectionSelection[] Items, long ExpectedRevision);
