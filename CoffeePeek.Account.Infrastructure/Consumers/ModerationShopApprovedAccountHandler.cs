using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Kernel;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Account.Infrastructure.Consumers;

public class ModerationShopApprovedAccountHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    ILogger<ModerationShopApprovedAccountHandler> logger)
{
    public async Task Handle(ModerationShopApprovedEvent message, CancellationToken ct)
    {
        logger.LogInformation("Received ModerationShopApprovedEvent for UserId: {UserId}", message.UserId);

        if (message.UserId is null)
        {
            logger.LogInformation("ModerationShopApprovedEvent has no submitting user (anonymous); skipping statistics update");
            return;
        }

        var user = await userRepository.GetById(message.UserId.Value, ct)
                   ?? throw new InvalidOperationException("User not found");

        user.Statistics.IncrementAddedShops();

        logger.LogInformation("Updated UserStatistics for UserId: {UserId}. New AddedShopsCount: {AddedShopsCount}",
            message.UserId, user.Statistics.AddedShopsCount);

        await unitOfWork.SaveChangesAsync(ct);
    }
}