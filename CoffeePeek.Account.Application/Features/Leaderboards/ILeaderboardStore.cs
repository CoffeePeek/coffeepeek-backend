using CoffeePeek.Contract.Dtos.Leaderboards;
using CoffeePeek.Contract.Events.Leaderboards;

namespace CoffeePeek.Account.Application.Features.Leaderboards;

public interface ILeaderboardStore
{
    Task ApplyAsync(ActivityScoreChanged message, CancellationToken ct);
    Task<LeaderboardPageDto> GetAsync(LeaderboardPeriod period, int page, int pageSize, Guid? viewerId, CancellationToken ct);
}
