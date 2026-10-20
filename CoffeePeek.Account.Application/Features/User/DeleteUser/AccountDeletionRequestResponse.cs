using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

namespace CoffeePeek.Account.Application.Features.User.DeleteUser;

public record AccountDeletionRequestResponse(
    Guid RequestId,
    AccountDeletionRequestStatus Status,
    DateTime ExpiresAtUtc,
    DateTime ResendAvailableAtUtc)
{
    public static AccountDeletionRequestResponse From(AccountDeletionRequest request) =>
        new(request.Id, request.Status, request.ExpiresAtUtc, request.ResendAvailableAtUtc);
}
