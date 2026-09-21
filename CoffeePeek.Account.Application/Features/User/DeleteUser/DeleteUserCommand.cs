using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

namespace CoffeePeek.Account.Application.Features.User.DeleteUser;

public record DeleteUserCommand(
    Guid UserId,
    AccountDeletionReason Reason = AccountDeletionReason.NoLongerUsing,
    string? OtherReason = null);