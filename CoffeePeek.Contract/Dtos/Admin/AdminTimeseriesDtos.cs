namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Count of items for a single UTC calendar day.</summary>
public record AdminDailyCountDto(DateOnly Date, int Count);

/// <summary>
/// New-user registrations per UTC day. <see cref="NewUsers"/> contains exactly <see cref="Days"/>
/// consecutive entries ending today (UTC), ordered ascending; days without registrations are 0.
/// </summary>
public record AdminUsersTimeseriesDto(int Days, IReadOnlyList<AdminDailyCountDto> NewUsers);

/// <summary>
/// Shops-service activity per UTC day. Each series contains exactly <see cref="Days"/> consecutive
/// zero-filled entries ending today (UTC), ordered ascending. Reviews exclude soft-deleted ones.
/// </summary>
public record AdminShopsTimeseriesDto(
    int Days,
    IReadOnlyList<AdminDailyCountDto> NewShops,
    IReadOnlyList<AdminDailyCountDto> NewReviews,
    IReadOnlyList<AdminDailyCountDto> NewCheckIns);
