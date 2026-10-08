namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>
/// Shops-service activity per UTC day. Each series contains exactly <see cref="Days"/> consecutive
/// zero-filled entries ending today (UTC), ordered ascending. Check-ins exclude soft-deleted ones.
/// </summary>
public record AdminShopsTimeseriesDto(
    int Days,
    IReadOnlyList<AdminDailyCountDto> NewShops,
    IReadOnlyList<AdminDailyCountDto> NewCheckIns);
