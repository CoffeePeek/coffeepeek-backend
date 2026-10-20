using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Contract.Events.Moderation;
using Wolverine;

namespace CoffeePeek.Moderation.Infrastructure.Consumers;

public static class ShopChangeRequestApplyFailedHandler
{
    public static async Task Handle(
        ShopChangeRequestApplyFailedV1 message,
        IShopChangeRequestRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var request = await repository.GetByIdAsync(message.RequestId, ct);
        if (request is null || !request.MarkApplyFailed(message.Reason, message.FailedAtUtc))
            return;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new ModerationDecisionChanged(Guid.NewGuid(), request.SubmittedByUserId,
            ModerationSubmissionKind.ShopChange, request.Id, request.Status.ToString()));
    }
}
