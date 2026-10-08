namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public enum MenuOcrJobStatus
{
    Pending = 0,
    Running = 1,
    ResultStored = 2,
    Applied = 3,
    Stale = 4,
    Failed = 5
}
