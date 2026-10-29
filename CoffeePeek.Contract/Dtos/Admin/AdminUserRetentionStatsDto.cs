namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Login/refresh retention on exact UTC days after registration, over retained account/session history.</summary>
public record AdminUserRetentionStatsDto(
    DateOnly AsOfDateUtc,
    AdminRetentionMetricDto D1,
    AdminRetentionMetricDto D7,
    AdminRetentionMetricDto D30,
    AdminRetentionMetricDto AfterRegistration);
