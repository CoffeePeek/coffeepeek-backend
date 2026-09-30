using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;

namespace CoffeePeek.Account.Persistence.Repositories;

public class CachedUserRepository(
    IUserRepository decorated,
    ICacheService redisService) : IUserRepository
{
    public async Task Update(User user, CancellationToken ct = default)
    {
        await decorated.Update(user, ct);

        await redisService.RemoveAsync(CacheKey.User.Profile(user.Id));
        await redisService.RemoveAsync(CacheKey.User.Entity(user.Id));
    }

    public Task Add(User user, CancellationToken ct = default) => decorated.Add(user, ct);

    public async Task Delete(User user, CancellationToken ct = default)
    {
        await decorated.Delete(user, ct);
        await redisService.RemoveAsync(CacheKey.User.Profile(user.Id));
        await redisService.RemoveAsync(CacheKey.User.Entity(user.Id));
    }

    public Task<User?> GetByEmail(string email, CancellationToken ct) =>
        decorated.GetByEmail(email, ct);

    public Task<User?> GetByEmailConfirmToken(string requestToken, CancellationToken cancellationToken)
        => decorated.GetByEmailConfirmToken(requestToken, cancellationToken);

    public Task<User?> GetByPasswordResetToken(string resetToken, CancellationToken cancellationToken)
        => decorated.GetByPasswordResetToken(resetToken, cancellationToken);

    public Task<User?> GetByRefreshToken(string refreshToken, CancellationToken ct = default)
        => decorated.GetByRefreshToken(refreshToken, ct);

    Task<User?> IUserRepository.GetById(Guid userId, CancellationToken ct) => decorated.GetById(userId, ct);
}
