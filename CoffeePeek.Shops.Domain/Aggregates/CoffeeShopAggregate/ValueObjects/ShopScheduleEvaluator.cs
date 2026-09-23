namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

/// <summary>
/// Evaluates weekly shop schedules. Both the schedule and the supplied clock value use UTC.
/// An interval whose close time is earlier than its open time continues into the next UTC day.
/// </summary>
public static class ShopScheduleEvaluator
{
    public static bool IsOpenAtUtc(IEnumerable<ShopSchedule> schedules, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Schedule must be evaluated with a UTC DateTime.", nameof(utcNow));

        var scheduleList = schedules as IReadOnlyCollection<ShopSchedule> ?? schedules.ToArray();
        if (scheduleList.Count == 0)
            return true;

        var currentTime = utcNow.TimeOfDay;

        if (scheduleList.Any(today =>
                today.DayOfWeek == utcNow.DayOfWeek &&
                !today.IsClosed &&
                today.Intervals.Any(interval => IsOpenDuringStartingDay(interval, currentTime))))
        {
            return true;
        }

        var previousDay = PreviousDay(utcNow.DayOfWeek);
        return scheduleList.Any(yesterday =>
            yesterday.DayOfWeek == previousDay &&
            !yesterday.IsClosed &&
            yesterday.Intervals.Any(interval =>
                CrossesUtcMidnight(interval) && currentTime <= interval.CloseTime));
    }

    private static bool IsOpenDuringStartingDay(ShopScheduleInterval interval, TimeSpan currentTime) =>
        CrossesUtcMidnight(interval)
            ? currentTime >= interval.OpenTime
            : currentTime >= interval.OpenTime && currentTime <= interval.CloseTime;

    private static bool CrossesUtcMidnight(ShopScheduleInterval interval) =>
        interval.CloseTime < interval.OpenTime;

    private static DayOfWeek PreviousDay(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? DayOfWeek.Saturday : dayOfWeek - 1;
}
