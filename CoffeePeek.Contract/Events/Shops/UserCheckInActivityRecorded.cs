namespace CoffeePeek.Contract.Events.Shops;

public sealed record UserCheckInActivityRecorded(Guid UserId, DateTime CreatedAtUtc);
