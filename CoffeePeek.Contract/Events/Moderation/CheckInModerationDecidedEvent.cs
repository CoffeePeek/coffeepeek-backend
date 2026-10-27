using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Events.Moderation;

public record CheckInModerationDecidedEvent(Guid CheckInId, int ContentRevision, Guid SubmissionId,
    long DecisionSequence, ModerationStatus Status, string? RejectionReason, DateTime DecidedAtUtc,
    bool? LeaderboardOverride = null);
