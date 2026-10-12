namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public record ShopSchedule
{
    public DayOfWeek DayOfWeek { get; private set; }
    public bool IsClosed { get; private set; }
    private List<ShopScheduleInterval> _intervals = [];
    public ICollection<ShopScheduleInterval> Intervals => _intervals.AsReadOnly();

    // ReSharper disable once UnusedMember.Local
    private ShopSchedule()
    {
    }

    internal ShopSchedule(
        DayOfWeek dayOfWeek,
        bool isClosed,
        ICollection<ShopScheduleInterval> intervals)
    {
        DayOfWeek = dayOfWeek;
        IsClosed = isClosed;
        ArgumentNullException.ThrowIfNull(intervals);
        _intervals = intervals.ToList();
    }

    public static ShopSchedule Create(DayOfWeek dayOfWeek, bool isClosed, ICollection<ShopScheduleInterval> intervals)
    {
        return new ShopSchedule(dayOfWeek, isClosed, intervals);
    }
}
