using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.Features.Profiles;

public sealed class ProfileActivityMonth
{
    public int Year { get; }
    public int Month { get; }
    public TimeZoneInfo TimeZone { get; }
    public DateTime UtcFrom { get; }
    public DateTime UtcTo { get; }

    private ProfileActivityMonth(int year, int month, TimeZoneInfo timeZone)
    {
        Year = year;
        Month = month;
        TimeZone = timeZone;
        var localStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        UtcFrom = BoundaryUtc(localStart, timeZone);
        UtcTo = BoundaryUtc(localStart.AddMonths(1), timeZone);
    }

    public static ProfileActivityMonth Parse(int year, int month, string? timeZone = "UTC")
    {
        if (year is < 2000 or > 2100)
            throw new ValidationException("Year must be between 2000 and 2100.");
        if (month is < 1 or > 12)
            throw new ValidationException("Month must be between 1 and 12.");
        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > 128)
            throw new ValidationException("Supply a valid time zone ID, for example UTC or Europe/Minsk.");

        try
        {
            return new ProfileActivityMonth(year, month, TimeZoneInfo.FindSystemTimeZoneById(timeZone));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ValidationException("Unknown or invalid time zone ID.");
        }
    }

    internal static DateTime BoundaryUtc(DateTime localBoundary, TimeZoneInfo timeZone)
    {
        // Historical base-offset changes can skip/repeat midnight without being reported
        // by IsInvalidTime/IsAmbiguousTime. Verify each possible offset using UTC -> local.
        var offsets = new HashSet<TimeSpan> { timeZone.BaseUtcOffset };
        foreach (var rule in timeZone.GetAdjustmentRules())
        {
            var standardOffset = timeZone.BaseUtcOffset + rule.BaseUtcOffsetDelta;
            offsets.Add(standardOffset);
            offsets.Add(standardOffset + rule.DaylightDelta);
        }

        DateTime? earliestMatch = null;
        DateTime? before = null;
        DateTime? after = null;
        foreach (var offset in offsets)
        {
            var candidate = DateTime.SpecifyKind(localBoundary - offset, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(candidate, timeZone);
            if (local == localBoundary)
            {
                // Include both occurrences of a repeated midnight at the start, and
                // exclude both at the end, by choosing its earliest UTC occurrence.
                if (earliestMatch is null || candidate < earliestMatch.Value)
                    earliestMatch = candidate;
            }
            else if (local < localBoundary)
            {
                if (before is null || candidate > before.Value)
                    before = candidate;
            }
            else if (after is null || candidate < after.Value)
            {
                after = candidate;
            }
        }

        if (earliestMatch is not null)
            return earliestMatch.Value;
        if (before is null || after is null || before >= after)
            throw new InvalidTimeZoneException("Cannot resolve the local month boundary.");

        // Midnight is in a forward gap. Find the first valid instant of the month,
        // including transitions that skip only part of the hour around midnight.
        // System zone transitions have second precision. Windows historical rules may
        // end at :59.999; do not treat that inclusive end as the next month beginning.
        var lower = before.Value.Ticks / TimeSpan.TicksPerSecond;
        var upper = after.Value.Ticks / TimeSpan.TicksPerSecond;
        while (upper - lower > 1)
        {
            var middle = lower + (upper - lower) / 2;
            if (TimeZoneInfo.ConvertTimeFromUtc(new DateTime(middle * TimeSpan.TicksPerSecond, DateTimeKind.Utc), timeZone) < localBoundary)
                lower = middle;
            else
                upper = middle;
        }

        return new DateTime(upper * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }
}
