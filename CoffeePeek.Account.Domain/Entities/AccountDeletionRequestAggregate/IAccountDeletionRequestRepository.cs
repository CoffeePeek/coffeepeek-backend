namespace CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

public interface IAccountDeletionRequestRepository
{
    Task<AccountDeletionRequest?> GetActiveByUserId(Guid userId, CancellationToken ct = default);
    Task<AccountDeletionRequest?> GetByTokenHash(string tokenHash, CancellationToken ct = default);
    Task Add(AccountDeletionRequest request, CancellationToken ct = default);
}
