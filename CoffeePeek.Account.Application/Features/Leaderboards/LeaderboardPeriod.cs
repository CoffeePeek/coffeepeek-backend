using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Application.Features.Leaderboards;

public sealed record LeaderboardPeriod(string Kind, DateOnly StartsOn, DateOnly EndsOnExclusive,
    DateTime StartsAtUtc, DateTime EndsAtUtc)
{
    public const string TimeZoneId = "Europe/Minsk";

    public static LeaderboardPeriod Create(string kind, DateOnly? date, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var day = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        if (day.Year is < 2000 or > 2100)
            throw new ValidationException("Date must be between 2000 and 2100.");
        var start = kind switch
        {
            "week" => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            "month" => new DateOnly(day.Year, day.Month, 1),
            _ => throw new ValidationException("Period must be week or month.")
        };
        var end = kind == "week" ? start.AddDays(7) : start.AddMonths(1);
        return new(kind, start, end,
            TimeZoneInfo.ConvertTimeToUtc(start.ToDateTime(TimeOnly.MinValue), zone),
            TimeZoneInfo.ConvertTimeToUtc(end.ToDateTime(TimeOnly.MinValue), zone));
    }
}
