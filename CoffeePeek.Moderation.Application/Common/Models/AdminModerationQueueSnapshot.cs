namespace CoffeePeek.Moderation.Application.Common.Models;

public record AdminModerationQueueSnapshot(string Queue, int Pending, DateTime? OldestPendingCreatedAtUtc);
