using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Shared.Domain.Interfaces.Persistance;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Persistence.Repositories;

public class UserRepository(AccountDbContext dbContext) : IUserRepository
{
    private readonly DbSet<User> _repository = dbContext.Users;
    public async Task<User?> GetById(Guid userId, CancellationToken ct = default)
    {
        return await _repository
            .Include(x => x.RefreshTokens)
            .Include(x => x.Roles)
            .Include(x => x.PhotoMetadata)
            .FirstOrDefaultAsync(c => c.Id == userId, ct);
    }

    public async Task Add(User user, CancellationToken ct = default)
    {
        await _repository.AddAsync(user, ct);
    }

    public Task Update(User user, CancellationToken ct = default)
    {
        _repository.Update(user);
        return Task.CompletedTask;
    }

    public Task Delete(User user, CancellationToken ct = default)
    {
        _repository.Remove(user);
        return Task.CompletedTask;
    }

    public Task<User?> GetByEmail(string email, CancellationToken ct)
    {
        return _repository
            .Include(x => x.Roles)
            .Include(x => x.RefreshTokens)
            .FirstOrDefaultAsync(c => c.Credentials.Email == email, ct);
    }

    public Task<User?> GetByEmailConfirmToken(string requestToken, CancellationToken cancellationToken)
    {
        return _repository.FirstOrDefaultAsync(c => c.Credentials.EmailConfirmationToken == requestToken,
            cancellationToken);
    }

    public Task<User?> GetByPasswordResetToken(string resetToken, CancellationToken cancellationToken)
    {
        return _repository
            .Include(x => x.RefreshTokens)
            .FirstOrDefaultAsync(c => c.Credentials.PasswordResetToken == resetToken, cancellationToken);
    }

    public Task<User?> GetByRefreshToken(string refreshToken, CancellationToken ct = default)
    {
        return _repository
            .Include(x => x.RefreshTokens)
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(u => u.RefreshTokens.Any(t => t.Token == refreshToken), ct);
    }
}
