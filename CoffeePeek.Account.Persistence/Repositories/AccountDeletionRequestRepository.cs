using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Persistence.Repositories;

public class AccountDeletionRequestRepository(AccountDbContext dbContext) : IAccountDeletionRequestRepository
{
    private readonly DbSet<AccountDeletionRequest> _repository = dbContext.AccountDeletionRequests;

    public Task<AccountDeletionRequest?> GetActiveByUserId(Guid userId, CancellationToken ct = default) =>
        _repository.FirstOrDefaultAsync(
            x => x.UserId == userId &&
                 (x.Status == AccountDeletionRequestStatus.PendingConfirmation ||
                  x.Status == AccountDeletionRequestStatus.Processing),
            ct);

    public Task<AccountDeletionRequest?> GetPendingByTokenHash(string tokenHash, CancellationToken ct = default) =>
        _repository.FirstOrDefaultAsync(
            x => x.ConfirmationTokenHash == tokenHash &&
                 x.Status == AccountDeletionRequestStatus.PendingConfirmation,
            ct);

    public async Task Add(AccountDeletionRequest request, CancellationToken ct = default) =>
        await _repository.AddAsync(request, ct);

    public Task Update(AccountDeletionRequest request, CancellationToken ct = default)
    {
        _repository.Update(request);
        return Task.CompletedTask;
    }
}
