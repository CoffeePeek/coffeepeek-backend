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

    private static DateTime BoundaryUtc(DateTime localBoundary, TimeZoneInfo timeZone)
    {
        // A month can start during a skipped midnight. Begin at its first valid instant.
        while (timeZone.IsInvalidTime(localBoundary))
            localBoundary = localBoundary.AddMinutes(1);

        // If midnight repeats, include both occurrences at the start and exclude both at the end.
        if (timeZone.IsAmbiguousTime(localBoundary))
            return new DateTimeOffset(localBoundary, timeZone.GetAmbiguousTimeOffsets(localBoundary).Max()).UtcDateTime;

        return TimeZoneInfo.ConvertTimeToUtc(localBoundary, timeZone);
    }
}
