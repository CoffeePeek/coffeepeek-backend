using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.User.DeleteUser;

public static class GetCurrentAccountDeletionRequestHandler
{
    public static async Task<Response<AccountDeletionRequestResponse>> Handle(
        GetCurrentAccountDeletionRequestQuery query,
        IAccountDeletionRequestRepository repository,
        CancellationToken ct)
    {
        var request = await repository.GetActiveByUserId(query.UserId, ct);
        if (request is null)
            return Response<AccountDeletionRequestResponse>.Error(
                System.Net.HttpStatusCode.NotFound,
                "No active account deletion request was found.");

        return Response<AccountDeletionRequestResponse>.Success(
            AccountDeletionRequestResponse.From(request));
    }
}
