namespace CoffeePeek.Contract.Dtos.Admin;

public record AdminServiceStatsDto(
    int TotalCoffeeShops = 0,
    int TotalCheckIns = 0,
    int NewCoffeeShopsToday = 0,
    int NewCheckInsToday = 0,
    int PendingModerationShops = 0,
    int PendingModerationCheckIns = 0,
    int ImportPending = 0,
    int ImportPublished = 0,
    int ImportRejected = 0,
    int ImportSkipped = 0,
    int ImportInFeed = 0);
