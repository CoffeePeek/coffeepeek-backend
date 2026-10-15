namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public partial record UserStatistics
{
    public int CheckInCount { get; private set; }
    public int AddedShopsCount { get; private set; }
    public int AddedRoastersCount { get; private set; }
    public DateTime StatisticUpdatedAtUtc { get; set; } = DateTime.UtcNow;

    private UserStatistics()
    {
        //ef core
    }

    internal UserStatistics(int checkInCount, int addedShopsCount)
    {
        CheckInCount = checkInCount;
        AddedShopsCount = addedShopsCount;
    }

    public static UserStatistics Empty() => new(0, 0);
    public void IncrementCheckIn()
    {
        CheckInCount += 1;
    }
    public void IncrementAddedShops()
    {
        AddedShopsCount += 1;
    }
    public void IncrementAddedRoasters()
    {
        AddedRoastersCount += 1;
        StatisticUpdatedAtUtc = DateTime.UtcNow;
    }
}
