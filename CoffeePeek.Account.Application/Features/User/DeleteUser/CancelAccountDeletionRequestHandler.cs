using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Shared.Domain.Interfaces.Persistance;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using System.Net;

namespace CoffeePeek.Account.Application.Features.User.DeleteUser;

public static class CancelAccountDeletionRequestHandler
{
    public static async Task<Response> Handle(
        CancelAccountDeletionRequestCommand command,
        IAccountDeletionRequestRepository repository,
        IAccountDeletionTokenService tokenService,
        TimeProvider timeProvider,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        string tokenHash;
        try
        {
            tokenHash = tokenService.Hash(command.Token);
        }
        catch (ArgumentException)
        {
            return Response.Error((int)HttpStatusCode.BadRequest, "A deletion confirmation token is required.");
        }

        var request = await repository.GetByTokenHash(tokenHash, ct);
        if (request is null || request.Status != AccountDeletionRequestStatus.PendingConfirmation)
            return Response.Error((int)HttpStatusCode.NotFound, "Pending account deletion request not found.");

        try
        {
            request.Cancel(timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (DomainException)
        {
            return Response.Error((int)HttpStatusCode.Conflict, "The deletion request cannot be cancelled.");
        }

        await repository.Update(request, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Response.Success();
    }
}
