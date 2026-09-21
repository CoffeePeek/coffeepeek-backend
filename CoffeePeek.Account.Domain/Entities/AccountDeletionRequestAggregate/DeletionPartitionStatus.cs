namespace CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

public enum DeletionPartitionStatus
{
    NotStarted = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}
