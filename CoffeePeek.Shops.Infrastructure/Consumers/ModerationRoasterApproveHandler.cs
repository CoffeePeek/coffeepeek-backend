using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shops.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public class ModerationRoasterApproveHandler(
    ICreateRoasterFromModerationService createRoasterService,
    ILogger<ModerationRoasterApproveHandler> logger)
{
    public async Task Handle(ModerationRoasterApprovedEvent message, CancellationToken ct)
    {
        logger.LogInformation(
            "Received ModerationRoasterApprovedEvent for moderation roaster {ModerationRoasterId} ({RoasterName})",
            message.Roaster.Id,
            message.Roaster.Name);

        var roasterId = await createRoasterService.CreateRoasterFromApprovedEventAsync(
            message.Roaster,
            moderationId: message.Roaster.Id,
            ct);

        logger.LogInformation(
            "Created published roaster {RoasterId} from moderation roaster {ModerationRoasterId}",
            roasterId,
            message.Roaster.Id);
    }
}
