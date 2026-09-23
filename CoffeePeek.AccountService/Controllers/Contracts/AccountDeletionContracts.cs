using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

namespace CoffeePeek.AccountService.Controllers.Contracts;

public record ConfirmAccountDeletionRequest(
    string Token,
    AccountDeletionReason Reason,
    string? OtherReason = null);
