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
    [InlineData(2017, 10, "America/Asuncion", "2017-10-01T04:00:00Z", "2017-11-01T03:00:00Z")]
    public void BoundariesHonorOffsetsAndMidnightTransitions(int year, int month, string zone, string from, string to)
    {
        var period = ProfileActivityMonth.Parse(year, month, zone);
        Assert.Equal(DateTimeOffset.Parse(from).UtcDateTime, period.UtcFrom);
        Assert.Equal(DateTimeOffset.Parse(to).UtcDateTime, period.UtcTo);
    }
}
