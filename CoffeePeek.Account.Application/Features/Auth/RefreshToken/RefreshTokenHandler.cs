using System.Net;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Auth.RefreshToken;

public class RefreshTokenHandler
{
    public const string TokenReusedMessage = "Security breach: Token reused.";

    public static async Task<Response<RefreshTokenResponse>> Handle(
        RefreshTokenCommand request,
        IUserRepository repository,
        IJWTTokenService tokenService,
        IOptions<JWTOptions> jwtOptions,
        IRevokedUserStore revokedUserStore,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await repository.GetByRefreshToken(request.RefreshToken, ct)
                   ?? throw new UnauthorizedException("Invalid refresh token");

        user.EnsureCanAuthenticate();

        var newRefreshTokenValue = tokenService.GenerateRefreshToken();

        var rotated = user.RotateRefreshToken(
            request.RefreshToken,
            newRefreshTokenValue,
            ttl: TimeSpan.FromDays(jwtOptions.Value.RefreshTokenLifetimeDays),
            request.DeviceName,
            request.IpAddress);

        if (!rotated)
        {
            // Return instead of throw so the transaction commits the session revocation.
            await unitOfWork.SaveChangesAsync(ct);
            await revokedUserStore.RevokeAccessTokensAsync(user.Id, jwtOptions.Value, ct);
            return Response<RefreshTokenResponse>.Error(HttpStatusCode.Unauthorized, TokenReusedMessage);
        }

        var newAccessToken = tokenService.GenerateAccessToken(user);
        await unitOfWork.SaveChangesAsync(ct);

        return Response<RefreshTokenResponse>.Success(new RefreshTokenResponse(
            newAccessToken,
            newRefreshTokenValue,
            DateTime.UtcNow.AddMinutes(jwtOptions.Value.AccessTokenLifetimeMinutes)));
    }
}
