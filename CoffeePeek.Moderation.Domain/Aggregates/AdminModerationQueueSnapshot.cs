namespace CoffeePeek.Moderation.Domain.Aggregates;

public record AdminModerationQueueSnapshot(string Queue, int Pending, DateTime? OldestPendingCreatedAtUtc);
