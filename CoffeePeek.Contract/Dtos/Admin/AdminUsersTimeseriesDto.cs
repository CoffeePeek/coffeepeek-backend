namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>
/// New-user registrations per UTC day. <see cref="NewUsers"/> contains exactly <see cref="Days"/>
/// consecutive entries ending today (UTC), ordered ascending; days without registrations are 0.
/// </summary>
public record AdminUsersTimeseriesDto(int Days, IReadOnlyList<AdminDailyCountDto> NewUsers);
