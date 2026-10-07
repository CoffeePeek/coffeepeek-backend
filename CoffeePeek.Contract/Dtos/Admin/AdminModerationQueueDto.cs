namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>A moderation queue's pending count and oldest pending item age in hours (null when empty).</summary>
public record AdminModerationQueueDto(string Queue, int Pending, double? OldestPendingHours);
