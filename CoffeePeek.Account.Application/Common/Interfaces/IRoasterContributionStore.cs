namespace CoffeePeek.Account.Application.Common.Interfaces;

public interface IRoasterContributionStore
{
    /// <summary>Atomically credits an existing author once for an approved roaster; replays are harmless.</summary>
    Task CreditAsync(Guid userId, Guid roasterId, CancellationToken ct);
}
