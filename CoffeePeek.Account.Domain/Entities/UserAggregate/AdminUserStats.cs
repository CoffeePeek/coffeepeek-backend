namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public record AdminUserStats(
    int TotalUsers,
    int ActiveUsers,
    int BlockedUsers,
    int RegisteredToday,
    IReadOnlyDictionary<string, int> UsersByRole,
    int DeletedUsers = 0,
    int DailyActiveUsers = 0,
    int WeeklyActiveUsers = 0,
    int MonthlyActiveUsers = 0,
    int EmailConfirmedUsers = 0,
    int GoogleUsers = 0,
    int AppleUsers = 0,
    int EmailUsers = 0,
    int OtherAuthUsers = 0,
    AdminUserRetentionStats? Retention = null);
