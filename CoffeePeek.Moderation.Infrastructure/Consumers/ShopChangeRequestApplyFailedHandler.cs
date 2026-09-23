using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.Moderation.Infrastructure.Consumers;

public static class ShopChangeRequestApplyFailedHandler
{
    public static async Task Handle(
        ShopChangeRequestApplyFailedV1 message,
        IShopChangeRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var request = await repository.GetByIdAsync(message.RequestId, ct);
        if (request is null || !request.MarkApplyFailed(message.Reason, message.FailedAtUtc))
            return;

        await unitOfWork.SaveChangesAsync(ct);
    }
}
