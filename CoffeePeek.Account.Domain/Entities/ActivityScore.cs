using CoffeePeek.Shared.Kernel.Leaderboards;

namespace CoffeePeek.Account.Domain.Entities;

public sealed class ActivityScore
{
    public LeaderboardActivityKind Kind { get; set; }
    public Guid SourceId { get; set; }
    public Guid UserId { get; set; }
    public long Version { get; set; }
    public int Points { get; set; }
    public DateTime? CreditedAtUtc { get; set; }
}
