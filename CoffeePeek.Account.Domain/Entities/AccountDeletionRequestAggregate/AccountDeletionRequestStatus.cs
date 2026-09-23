namespace CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

public enum AccountDeletionRequestStatus
{
    PendingConfirmation = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
