using System;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Profiles;

namespace CoffeePeek.Shops.Application.Tests.Features.Profiles;

public sealed class ProfileActivityMonthTests
{
    [Theory]
    [InlineData(1999, 1)]
    [InlineData(2101, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void RejectsInvalidPeriod(int year, int month) =>
        Assert.Throws<ValidationException>(() => ProfileActivityMonth.Parse(year, month));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("NoSuch/TimeZone")]
    public void RejectsInvalidZone(string? timeZone) =>
        Assert.Throws<ValidationException>(() => ProfileActivityMonth.Parse(2026, 10, timeZone));

    [Fact]
    public void RejectsUnboundedZoneInput() =>
        Assert.Throws<ValidationException>(() => ProfileActivityMonth.Parse(2026, 10, new string('a', 129)));

    [Fact]
    public void DefaultsToUtc()
    {
        var month = ProfileActivityMonth.Parse(2026, 10);
        Assert.Equal("UTC", month.TimeZone.Id);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), month.UtcFrom);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), month.UtcTo);
    }

    [Theory]
    [InlineData(2025, 3, "America/New_York", "2025-03-01T05:00:00Z", "2025-04-01T04:00:00Z")]
    [InlineData(2025, 11, "America/New_York", "2025-11-01T04:00:00Z", "2025-12-01T05:00:00Z")]
    [InlineData(2025, 9, "Europe/Minsk", "2025-08-31T21:00:00Z", "2025-09-30T21:00:00Z")]
    [InlineData(2026, 11, "America/Havana", "2026-11-01T04:00:00Z", "2026-12-01T05:00:00Z")]
    [InlineData(2017, 9, "America/Asuncion", "2017-09-01T04:00:00Z", "2017-10-01T04:00:00Z")]
    [InlineData(2017, 10, "America/Asuncion", "2017-10-01T04:00:00Z", "2017-11-01T03:00:00Z")]
    public void BoundariesHonorOffsetsAndMidnightTransitions(int year, int month, string zone, string from, string to)
    {
        var period = ProfileActivityMonth.Parse(year, month, zone);
        Assert.Equal(DateTimeOffset.Parse(from).UtcDateTime, period.UtcFrom);
        Assert.Equal(DateTimeOffset.Parse(to).UtcDateTime, period.UtcTo);
    }

    [Fact]
    public void UsesEarliestMidnightWhenHistoricalBaseOffsetRepeatsIt()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 1);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 11, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2000, 1, 1), new DateTime(2017, 9, 30), TimeSpan.Zero, start, end, TimeSpan.FromHours(1));
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "HistoricalBaseOffsetFold", TimeSpan.FromHours(-3), "Historical fold", "Standard", "Daylight", [rule]);
        var midnight = new DateTime(2017, 10, 1);

        var boundary = ProfileActivityMonth.BoundaryUtc(midnight, zone);

        Assert.Equal(new DateTime(2017, 10, 1, 2, 0, 0, DateTimeKind.Utc), boundary);
        Assert.Equal(midnight, TimeZoneInfo.ConvertTimeFromUtc(boundary, zone));
        Assert.Equal(midnight, TimeZoneInfo.ConvertTimeFromUtc(boundary.AddHours(1), zone));
    }

    [Theory]
    [InlineData(10, 1, 0, 0, 60, "2017-10-01T04:00:00Z")]
    [InlineData(9, 30, 23, 30, 60, "2017-10-01T03:30:00Z")]
    [InlineData(10, 1, 0, 0, 30, "2017-10-01T04:00:00Z")]
    public void SkippedMidnightStartsAtTheTransitionInstant(
        int transitionMonth, int transitionDay, int hour, int minute, int deltaMinutes, string expected)
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            new DateTime(1, 1, 1, hour, minute, 0), transitionMonth, transitionDay);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 2);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2017, 1, 1), new DateTime(2017, 12, 31), TimeSpan.FromMinutes(deltaMinutes), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "MidnightGap", TimeSpan.FromHours(-4), "Midnight gap", "Standard", "Daylight", [rule]);
        var midnight = new DateTime(2017, 10, 1);

        var boundary = ProfileActivityMonth.BoundaryUtc(midnight, zone);

        Assert.Equal(DateTimeOffset.Parse(expected).UtcDateTime, boundary);
        Assert.True(TimeZoneInfo.ConvertTimeFromUtc(boundary.AddTicks(-1), zone) < midnight);
        Assert.True(TimeZoneInfo.ConvertTimeFromUtc(boundary, zone) >= midnight);
    }
}
