using CoffeePeek.Account.Application.Features.Leaderboards;
using CoffeePeek.Contract.Events.Leaderboards;

namespace CoffeePeek.Account.Infrastructure.Consumers;

public sealed class ActivityScoreChangedHandler(ILeaderboardStore store)
{
    public Task Handle(ActivityScoreChanged message, CancellationToken ct) => store.ApplyAsync(message, ct);
}
