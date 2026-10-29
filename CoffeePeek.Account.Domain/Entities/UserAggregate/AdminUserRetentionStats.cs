namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public record AdminUserRetentionStats(
    DateOnly AsOfDateUtc,
    AdminRetentionMetric D1,
    AdminRetentionMetric D7,
    AdminRetentionMetric D30,
    AdminRetentionMetric AfterRegistration);
