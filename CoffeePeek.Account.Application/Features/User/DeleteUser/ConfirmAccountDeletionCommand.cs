using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

namespace CoffeePeek.Account.Application.Features.User.DeleteUser;

public record ConfirmAccountDeletionCommand(
    string Token,
    AccountDeletionReason Reason,
    string? OtherReason = null);
