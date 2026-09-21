namespace CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

public enum AccountDeletionReason
{
    PrivacyConcerns = 0,
    NoLongerUsing = 1,
    DuplicateAccount = 2,
    UnsatisfactoryExperience = 3,
    Other = 4
}
