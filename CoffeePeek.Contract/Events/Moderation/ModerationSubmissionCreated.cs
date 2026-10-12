namespace CoffeePeek.Contract.Events.Moderation;

public sealed record ModerationSubmissionCreated(
    ModerationSubmissionKind Kind,
    Guid SubmissionId,
    Guid? UserId,
    string? Subject = null,
    Guid? RelatedId = null,
    string? Details = null);
