namespace CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

public sealed record ImportRejectedByReasonStats(
    int Closed,
    int Invalid,
    int NotCoffee,
    int Duplicate,
    int Unspecified);
