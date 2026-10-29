namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public record AdminRetentionMetric(int EligibleUsers, int ReturnedUsers)
{
    public decimal? RatePercent => EligibleUsers == 0
        ? null
        : Math.Round(100m * ReturnedUsers / EligibleUsers, 2, MidpointRounding.AwayFromZero);
}
