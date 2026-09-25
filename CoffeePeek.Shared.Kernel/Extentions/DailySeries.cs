namespace CoffeePeek.Shared.Kernel.Extentions;

/// <summary>Helpers for building zero-filled per-day (UTC) series for admin statistics.</summary>
public static class DailySeries
{
    public const int DefaultDays = 30;
    public const int MaxDays = 90;

    /// <summary>Returns <paramref name="days"/> or 30 when null, clamped to 1..90.</summary>
    public static int ClampDays(int? days) => Math.Clamp(days ?? DefaultDays, 1, MaxDays);

    /// <summary>
    /// Returns <paramref name="days"/> consecutive ascending entries ending at <paramref name="today"/>;
    /// days missing from <paramref name="counts"/> are 0.
    /// </summary>
    public static IReadOnlyList<(DateOnly Date, int Count)> Fill(
        DateOnly today,
        int days,
        IReadOnlyDictionary<DateOnly, int> counts)
    {
        if (days < 1) days = 1;

        var result = new List<(DateOnly Date, int Count)>(days);
        var start = today.AddDays(-(days - 1));
        for (var i = 0; i < days; i++)
        {
            var date = start.AddDays(i);
            result.Add((date, counts.TryGetValue(date, out var count) ? count : 0));
        }

        return result;
    }

    /// <summary>First UTC instant of the window that ends today and spans <paramref name="days"/> days.</summary>
    public static DateTime WindowStartUtc(DateTime todayUtc, int days) => todayUtc.Date.AddDays(-(days - 1));
}
