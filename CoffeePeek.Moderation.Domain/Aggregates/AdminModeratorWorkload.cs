namespace CoffeePeek.Moderation.Domain.Aggregates;

public record AdminModeratorWorkload(Guid ModeratorUserId, int Total, int Approved, int Rejected);
