using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Auth.Login;

public static class LoginUserHandler
{
    public static async Task<Response<LoginResponse>> Handle(
        LoginUserCommand request,
        IUserRepository userRepository,
        IPasswordHasherService passwordHasher,
        IJWTTokenService tokens,
        IOptions<JWTOptions> jwtOptions,
        CancellationToken ct)
    {
        var user = await userRepository.GetByEmail(request.Email, ct);
        if (user is null || !user.Credentials.ValidatePassword(request.Password, passwordHasher))
            throw new UnauthorizedException("Invalid credentials");
        if (!user.Credentials.EmailConfirmed)
            throw new UnauthorizedException("Email is not confirmed");

        // Session persistence (AddSession) is managed by Wolverine's transaction.
        var (accessToken, refreshToken, expiresAt) =
            AuthSession.Issue(user, tokens, jwtOptions.Value, request.DeviceName, request.IpAddress);
        return Response<LoginResponse>.Success(new LoginResponse(accessToken, refreshToken, expiresAt));
    }
}
