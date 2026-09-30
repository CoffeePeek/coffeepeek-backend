namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public interface IUserRepository
{
    Task<User?> GetById(Guid userId, CancellationToken ct = default);
    Task Add(User user, CancellationToken ct = default);
    Task Update(User user, CancellationToken ct = default);
    Task Delete(User user, CancellationToken ct = default);
    Task<User?> GetByEmail(string email, CancellationToken ct);
    Task<User?> GetByEmailConfirmToken(string requestToken, CancellationToken cancellationToken);
    Task<User?> GetByPasswordResetToken(string resetToken, CancellationToken cancellationToken);
    Task<User?> GetByRefreshToken(string refreshToken, CancellationToken ct = default);
}
