using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

namespace CoffeePeek.Account.Application.Features.User.DeleteUser;

public record AccountDeletionRequestResponse(
    Guid RequestId,
    AccountDeletionRequestStatus Status,
    DateTime ExpiresAtUtc,
    DateTime ResendAvailableAtUtc);
