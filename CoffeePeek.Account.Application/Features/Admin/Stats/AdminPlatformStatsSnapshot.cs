namespace CoffeePeek.Account.Application.Features.Admin.Stats;

public sealed record AdminPlatformStatsSnapshot(
    int TotalCoffeeShops,
    int TotalCheckIns,
    int NewCoffeeShopsToday,
    int NewCheckInsToday,
    int PendingModerationShops,
    int PendingModerationCheckIns,
    int ImportPending,
    int ImportPublished,
    int ImportRejected,
    int ImportSkipped,
    int ImportInFeed,
    bool ShopsAvailable,
    bool ModerationAvailable);
