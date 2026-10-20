namespace CoffeePeek.Contract.Events.Moderation;

public sealed record ModerationDecisionChanged(Guid EventId, Guid UserId,
    ModerationSubmissionKind Kind, Guid SubmissionId, string Status);
