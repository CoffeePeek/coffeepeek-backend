using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Contract.Events.Moderation;

namespace CoffeePeek.Account.Infrastructure.Consumers;

public sealed class ModerationRoasterApprovedAccountHandler(IRoasterContributionStore contributions)
{
    public Task Handle(ModerationRoasterApprovedEvent message, CancellationToken ct) =>
        message.UserId is { } userId
            ? contributions.CreditAsync(userId, message.Roaster.Id, ct)
            : Task.CompletedTask;
}
