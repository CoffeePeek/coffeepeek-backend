namespace CoffeePeek.Contract.Dtos.Leaderboards;

public sealed record LeaderboardPageDto(string Period, DateOnly StartsOn, DateOnly EndsOnExclusive,
    string TimeZone, IReadOnlyList<LeaderboardEntryDto> Items, int TotalCount, int Page, int PageSize,
    LeaderboardEntryDto? Me);
