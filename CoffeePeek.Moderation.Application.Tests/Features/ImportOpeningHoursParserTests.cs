using CoffeePeek.Moderation.Application.Features.Import;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ImportOpeningHoursParserTests
{
    [Theory]
    [InlineData("Mon 07:30-22:30; Tue 07:30-22:30")]
    [InlineData("Mo-Tu 07:30-22:30")]
    [InlineData("Понедельник07:30–22:30\nВторник07:30–22:30")]
    public void Parse_ConvertsSupportedLocalFormatsToUtc(string text)
    {
        var days = ImportOpeningHoursParser.Parse(text)!;
        days.Should().HaveCount(7);
        foreach (var day in days.Where(d => d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday))
        {
            day.IsClosed.Should().BeFalse();
            day.Intervals.Should().ContainSingle();
            day.Intervals![0].OpenTime.Should().Be(TimeSpan.FromHours(4.5));
            day.Intervals[0].CloseTime.Should().Be(TimeSpan.FromHours(19.5));
        }
        days.Single(d => d.DayOfWeek == DayOfWeek.Sunday).IsClosed.Should().BeTrue();
    }

    [Fact]
    public void Parse_ShiftsStartingDayAndPreservesOvernightAndMultipleIntervals()
    {
        var days = ImportOpeningHoursParser.Parse("Mo 01:00-05:00; Tu 08:00-12:00,13:00-22:00; We off")!;
        var sunday = days.Single(d => d.DayOfWeek == DayOfWeek.Sunday);
        sunday.Intervals![0].OpenTime.Should().Be(TimeSpan.FromHours(22));
        sunday.Intervals[0].CloseTime.Should().Be(TimeSpan.FromHours(2));
        days.Single(d => d.DayOfWeek == DayOfWeek.Tuesday).Intervals.Should().HaveCount(2);
        days.Single(d => d.DayOfWeek == DayOfWeek.Wednesday).IsClosed.Should().BeTrue();
    }

    [Theory]
    [InlineData("Mo 25:00-26:00")]
    [InlineData("Mo 08:00-08:00")]
    [InlineData("Mo 08:00-22:00; Mo 09:00-23:00")]
    [InlineData("Mo 08:00-22:00 unknown")]
    [InlineData("по договорённости")]
    [InlineData(";;;")]
    [InlineData("24/7")]
    [InlineData("Mo 00:00-24:00")]
    public void Parse_RejectsUnknownOrInvalidHoursWithoutPartialSchedule(string text)
    {
        Action parse = () => ImportOpeningHoursParser.Parse(text);
        parse.Should().Throw<DomainException>();
    }

    [Fact]
    public void Parse_MissingSourceHoursStayUnknown() => ImportOpeningHoursParser.Parse(null).Should().BeNull();
}
