using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

namespace CoffeePeek.AccountService.Controllers.Contracts;

public record RequestAccountDeletionRequest(
    AccountDeletionReason Reason = AccountDeletionReason.NoLongerUsing,
    string? OtherReason = null);
