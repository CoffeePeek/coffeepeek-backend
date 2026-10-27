namespace CoffeePeek.Moderation.Domain.Entities;

public enum ModerationAuditAction
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    LeaderboardIncluded = 3,
    LeaderboardExcluded = 4
}
