using CoffeePeek.Shared.Kernel.Leaderboards;

namespace CoffeePeek.Contract.Events.Leaderboards;

public sealed record ActivityScoreChanged(LeaderboardActivityKind Kind, Guid SourceId, Guid UserId,
    long Version, bool IsEligible, DateTime? CreditedAtUtc);
