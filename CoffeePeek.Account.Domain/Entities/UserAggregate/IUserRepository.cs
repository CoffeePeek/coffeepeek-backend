namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public interface IUserRepository
{
    Task<User?> GetById(Guid userId, CancellationToken ct = default);
    void Add(User user, CancellationToken ct);
    Task Delete(User user, CancellationToken ct = default);
    Task<User?> GetByEmail(string email, CancellationToken ct);
    Task<User?> GetByProvider(string provider, string providerId, CancellationToken ct);
    Task<User?> GetByEmailConfirmToken(string requestToken, CancellationToken cancellationToken);
    Task<User?> GetByPasswordResetToken(string resetToken, CancellationToken cancellationToken);
    Task<User?> GetByRefreshToken(string refreshToken, CancellationToken ct = default);
    Task<bool> UserExistsByEmail(string email, CancellationToken ct);
    Task<bool> IsUsernameUnique(string username, CancellationToken ct);
}
