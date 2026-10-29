using System.Globalization;
using System.Text.RegularExpressions;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Application.Features.Import;

/// <summary>Converts the Minsk import's local weekly hours to the UTC schedule wire contract.</summary>
public static class ImportOpeningHoursParser
{
    private static readonly Dictionary<string, DayOfWeek> Days = new(StringComparer.OrdinalIgnoreCase)
    {
        ["su"] = DayOfWeek.Sunday, ["sun"] = DayOfWeek.Sunday, ["воскресенье"] = DayOfWeek.Sunday,
        ["mo"] = DayOfWeek.Monday, ["mon"] = DayOfWeek.Monday, ["понедельник"] = DayOfWeek.Monday,
        ["tu"] = DayOfWeek.Tuesday, ["tue"] = DayOfWeek.Tuesday, ["вторник"] = DayOfWeek.Tuesday,
        ["we"] = DayOfWeek.Wednesday, ["wed"] = DayOfWeek.Wednesday, ["среда"] = DayOfWeek.Wednesday,
        ["th"] = DayOfWeek.Thursday, ["thu"] = DayOfWeek.Thursday, ["четверг"] = DayOfWeek.Thursday,
        ["fr"] = DayOfWeek.Friday, ["fri"] = DayOfWeek.Friday, ["пятница"] = DayOfWeek.Friday,
        ["sa"] = DayOfWeek.Saturday, ["sat"] = DayOfWeek.Saturday, ["суббота"] = DayOfWeek.Saturday
    };

    public static IReadOnlyList<ScheduleDto>? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > 2000) throw InvalidHours();

        var local = new Dictionary<DayOfWeek, List<(int Open, int Close)>>();
        text = text.Replace('–', '-').Replace('—', '-');
        foreach (var row in text.Split([';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(row, @"^([\p{L}]+)(?:\s*-\s*([\p{L}]+))?\s*:?[ ]*(.+)$", RegexOptions.CultureInvariant);
            if (!match.Success || !Days.TryGetValue(match.Groups[1].Value, out var first)) throw InvalidHours();
            var last = first;
            if (match.Groups[2].Success && !Days.TryGetValue(match.Groups[2].Value, out last)) throw InvalidHours();
            var intervals = new List<(int Open, int Close)>();
            var hours = match.Groups[3].Value.Trim();
            if (!hours.Equals("off", StringComparison.OrdinalIgnoreCase)
                && !hours.Equals("closed", StringComparison.OrdinalIgnoreCase)
                && !hours.Equals("выходной", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var part in hours.Split(',', StringSplitOptions.TrimEntries))
                {
                    var times = part.Split('-', StringSplitOptions.TrimEntries);
                    if (times.Length != 2) throw InvalidHours();
                    var open = Minutes(times[0], false);
                    var close = Minutes(times[1], true);
                    // shortcut: full-day expressions need an explicit supported wire representation before accepting them.
                    if (open == close || close - open == 1440) throw InvalidHours();
                    intervals.Add((open, close));
                }
            }
            for (var day = first; ; day = (DayOfWeek)(((int)day + 1) % 7))
            {
                if (!local.TryAdd(day, intervals)) throw InvalidHours();
                if (day == last) break;
            }
        }

        if (local.Count == 0) throw InvalidHours();
        var utc = Enum.GetValues<DayOfWeek>().ToDictionary(day => day, _ => new List<ShopScheduleIntervalDto>());
        foreach (var (day, intervals) in local)
        foreach (var (open, close) in intervals)
        {
            // Minsk imports use UTC+3 throughout the year; shift the starting day as well as the time.
            var utcDay = open < 180 ? (DayOfWeek)(((int)day + 6) % 7) : day;
            utc[utcDay].Add(new ShopScheduleIntervalDto
            {
                OpenTime = TimeSpan.FromMinutes((open - 180 + 1440) % 1440),
                CloseTime = TimeSpan.FromMinutes((close - 180 + 1440) % 1440)
            });
        }
        return utc.Select(pair => new ScheduleDto(pair.Key, pair.Value.Count == 0,
            pair.Value.OrderBy(i => i.OpenTime).ToList())).ToArray();
    }

    private static int Minutes(string value, bool closing)
    {
        if (closing && value == "24:00") return 1440;
        if (!TimeOnly.TryParseExact(value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time)) throw InvalidHours();
        return time.Hour * 60 + time.Minute;
    }

    private static DomainException InvalidHours() => new("Unsupported opening hours. Use weekly days and HH:mm-HH:mm intervals in Minsk local time.");
}
