namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>
/// Registrations and distinct users with a login/refresh per UTC calendar day. Both series contain
/// exactly <see cref="Days"/> ascending, zero-filled entries ending today (UTC); today is incomplete.
/// </summary>
public record AdminUsersTimeseriesDto(
    int Days,
    IReadOnlyList<AdminDailyCountDto> NewUsers,
    IReadOnlyList<AdminDailyCountDto> DailyActiveUsers);
