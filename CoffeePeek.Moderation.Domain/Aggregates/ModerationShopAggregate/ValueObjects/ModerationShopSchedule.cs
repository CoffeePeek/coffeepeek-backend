using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public record ModerationShopSchedule
{
    public DayOfWeek DayOfWeek { get; private set; }
    public bool IsClosed { get; private set; }
    private readonly List<ModerationShopScheduleInterval> _intervals = [];
    public IReadOnlyCollection<ModerationShopScheduleInterval> Intervals => _intervals.AsReadOnly();

    // ReSharper disable once UnusedMember.Local
    private ModerationShopSchedule() { }

    internal ModerationShopSchedule(
        DayOfWeek dayOfWeek,
        bool isClosed,
        IReadOnlyCollection<ModerationShopScheduleInterval> intervals)
    {
        if (!Enum.IsDefined(dayOfWeek))
            throw new DomainException("Unknown schedule day.");
        if (isClosed && intervals.Count != 0 || !isClosed && intervals.Count == 0)
            throw new DomainException("Open days require intervals and closed days cannot have intervals.");
        if (intervals.Any(i => i.OpenTime < TimeSpan.Zero || i.OpenTime >= TimeSpan.FromDays(1)
                              || i.CloseTime < TimeSpan.Zero || i.CloseTime > TimeSpan.FromDays(1)
                              || i.OpenTime == i.CloseTime))
            throw new DomainException("Invalid schedule interval.");
        DayOfWeek = dayOfWeek;
        IsClosed = isClosed;
        _intervals.AddRange(intervals);
    }
}
