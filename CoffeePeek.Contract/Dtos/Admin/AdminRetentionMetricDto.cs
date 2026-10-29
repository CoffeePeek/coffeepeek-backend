namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Rate is null when no registrations have a complete observation window.</summary>
public record AdminRetentionMetricDto(int EligibleUsers, int ReturnedUsers, decimal? RatePercent);
