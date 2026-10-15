using CoffeePeek.Contract.Dtos.ProfileActivity;
using CoffeePeek.Shops.Application.Features.Profiles;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

public sealed class ProfileActivityQueries(ShopsDbContext db) : IProfileActivityQueries
{
    public async Task<ProfileActivityDto> GetAsync(Guid authorId, ProfileActivityMonth month, CancellationToken ct = default)
    {
        // Filter in SQL and fetch only visit times. Grouping locally preserves the requested zone's DST rules.
        var visits = await VisitsInMonth(authorId, month).ToArrayAsync(ct);
        var counts = visits.GroupBy(visit => LocalVisitDate(visit, month.TimeZone))
            .ToDictionary(group => group.Key, group => group.Count());
        var days = Enumerable.Range(1, DateTime.DaysInMonth(month.Year, month.Month))
            .Select(day => new DateOnly(month.Year, month.Month, day))
            .Select(date => new ProfileActivityDayDto(date, counts.GetValueOrDefault(date)))
            .ToArray();
        return new ProfileActivityDto(month.Year, month.Month, month.TimeZone.Id,
            days.Count(day => day.CheckInCount > 0), days.Sum(day => day.CheckInCount), days);
    }

    private static DateOnly LocalVisitDate(DateTime visit, TimeZoneInfo timeZone)
    {
        // IANA transitions have second precision. Ignore Windows historical rules' inclusive
        // :59.999 end so a subsecond visit cannot be grouped into the next day prematurely.
        var wholeSecond = new DateTime(visit.Ticks - visit.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(wholeSecond, timeZone));
    }

    internal IQueryable<DateTime> VisitsInMonth(Guid authorId, ProfileActivityMonth month) =>
        CheckInEligibility.Public(db).AsNoTracking()
            .Where(checkIn => checkIn.UserId == authorId && checkIn.VisitedAt >= month.UtcFrom && checkIn.VisitedAt < month.UtcTo)
            .Select(checkIn => checkIn.VisitedAt);
}
