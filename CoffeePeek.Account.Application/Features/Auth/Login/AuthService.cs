using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Models;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Auth.Login;

public class AuthService(
    IJWTTokenService jwtTokenService,
    IUserRepository userRepository,
    IPasswordHasherService passwordHasher,
    IOptions<JWTOptions> jwtOptions) : IAuthService
{
    public async Task<AuthResult> LoginAsync(string email, string password, string device, string ip, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByEmail(email, cancellationToken);

        if (user == null)
            throw new UnauthorizedException("Invalid credentials");
        if (!user.Credentials.ValidatePassword(password, passwordHasher))
            throw new UnauthorizedException("Invalid credentials");

        if (!user.Credentials.EmailConfirmed)
        {
            throw new UnauthorizedException("Email is not confirmed");
        }

        user.EnsureCanAuthenticate();

        var accessToken = jwtTokenService.GenerateAccessToken(user);
        var refreshToken = jwtTokenService.GenerateRefreshToken();

        var session = user.AddSession(
            refreshToken,
            ttl: TimeSpan.FromDays(jwtOptions.Value.RefreshTokenLifetimeDays),
            device,
            ip,
            absoluteLifetime: TimeSpan.FromDays(jwtOptions.Value.RefreshTokenAbsoluteLifetimeDays));

        var authResult = new AuthResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiredAt = DateTime.UtcNow.AddMinutes(jwtOptions.Value.AccessTokenLifetimeMinutes),
            RefreshTokenExpiresAt = session.ExpiryDate
        };

        return authResult;
    }
}
