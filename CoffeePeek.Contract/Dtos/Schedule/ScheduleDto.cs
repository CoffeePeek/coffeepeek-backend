#nullable enable
namespace CoffeePeek.Contract.Dtos.Schedule;

/// <summary>
/// Opening hours for a UTC day. Clients must convert local opening hours, including the day of
/// week, to UTC before sending them and convert them back to local time for display.
/// </summary>
public record ScheduleDto(DayOfWeek DayOfWeek, bool IsClosed, List<ShopScheduleIntervalDto>? Intervals);
