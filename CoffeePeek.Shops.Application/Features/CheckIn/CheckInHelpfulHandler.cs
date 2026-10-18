using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class CheckInHelpfulHandler
{
    public static async Task<Response<CheckInHelpfulResponse>> Handle(SetCheckInHelpfulCommand command,
        IQueryCheckInRepository repository, ICheckInQueries queries, IMessageBus bus, CancellationToken ct)
    {
        var row = await queries.GetUnifiedByIdAsync(command.Id, command.UserId, ct);
        if (row == null) throw new NotFoundException("Check-in not found.");
        if (row.UserId == command.UserId) throw new ForbiddenException("You cannot mark your own check-in as helpful.");
        var vote = await repository.SetHelpfulAsync(command.Id, command.UserId, command.IsHelpful, ct);
        if (vote.Changed)
            await bus.PublishAsync(new CheckInCacheInvalidationRequested(command.Id, row.ShopId, row.UserId));
        return Response<CheckInHelpfulResponse>.Success(new(vote.IsHelpful, vote.HelpfulCount));
    }
}
