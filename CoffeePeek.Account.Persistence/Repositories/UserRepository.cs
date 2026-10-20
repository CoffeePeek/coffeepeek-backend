using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Persistence.Repositories;

// Loaded users are tracked; SaveChanges persists their changes without an explicit Update.
public class UserRepository(AccountDbContext dbContext) : IUserRepository
{
    private readonly DbSet<User> _repository = dbContext.Users;

    public Task<User?> GetById(Guid userId, CancellationToken ct = default) =>
        _repository
            .Include(x => x.RefreshTokens)
            .Include(x => x.Roles)
            .Include(x => x.PhotoMetadata)
            .FirstOrDefaultAsync(c => c.Id == userId, ct);

    public void Add(User user, CancellationToken ct) => _repository.Add(user);

    public Task Delete(User user, CancellationToken ct = default)
    {
        _repository.Remove(user);
        return Task.CompletedTask;
    }

    public Task<User?> GetByEmail(string email, CancellationToken ct) =>
        _repository
            .Include(x => x.Roles)
            .Include(x => x.RefreshTokens)
            .FirstOrDefaultAsync(c => c.Credentials.Email == email, ct);

    public Task<User?> GetByProvider(string provider, string providerId, CancellationToken ct) =>
        _repository
            .Include(x => x.Credentials)
            .Include(x => x.Roles)
            .Include(x => x.RefreshTokens)
            .FirstOrDefaultAsync(x => x.Credentials.OAuthProvider == provider && x.Credentials.ProviderId == providerId, ct);

    public Task<User?> GetByEmailConfirmToken(string requestToken, CancellationToken cancellationToken) =>
        _repository.FirstOrDefaultAsync(c => c.Credentials.EmailConfirmationToken == requestToken, cancellationToken);

    public Task<User?> GetByPasswordResetToken(string resetToken, CancellationToken cancellationToken) =>
        _repository
            .Include(x => x.RefreshTokens)
            .FirstOrDefaultAsync(c => c.Credentials.PasswordResetToken == resetToken, cancellationToken);

    public Task<User?> GetByRefreshToken(string refreshToken, CancellationToken ct = default) =>
        _repository
            .Include(x => x.RefreshTokens)
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(u => u.RefreshTokens.Any(t => t.Token == refreshToken), ct);

    public Task<bool> UserExistsByEmail(string email, CancellationToken ct) =>
        _repository.AnyAsync(c => c.Credentials.Email == email, ct);

    public async Task<bool> IsUsernameUnique(string username, CancellationToken ct) =>
        !await _repository.AnyAsync(u => u.Username == username, ct);
}
