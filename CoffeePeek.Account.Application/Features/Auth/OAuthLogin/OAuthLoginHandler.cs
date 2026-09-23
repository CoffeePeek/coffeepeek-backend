using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Auth.OAuthLogin;

public static class GoogleLoginHandler
{
    public static async Task<Response<GoogleLoginResponse>> Handle(
        GoogleLoginCommand request,
        IGoogleAuthService googleAuthService,
        IExternalAuthService externalAuthService,
        IJWTTokenService tokenService,
        IOptions<JWTOptions> options,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var payload = await googleAuthService.ValidateIdTokenAsync(request.IdToken, ct);
        if (payload is null)
            return Response<GoogleLoginResponse>.Error("Invalid token");

        if (payload.EmailVerified is not true)
            return Response<GoogleLoginResponse>.Error(System.Net.HttpStatusCode.BadRequest, "Google email is not verified.");

        var user = await externalAuthService.GetOrCreate(
            payload.Email,
            ProviderConsts.GoogleProvider,
            providerId: payload.Subject,
            ct);

        user.EnsureCanAuthenticate();

        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();

        user.AddSession(
            refreshToken,
            ttl: TimeSpan.FromDays(options.Value.RefreshTokenLifetimeDays),
            request.DeviceName,
            request.IpAddress);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            // Concurrent OAuth login with the same Google account created the user first.
            // Re-fetch the saved user, issue a fresh session for this login attempt.
            user = await externalAuthService.GetOrCreate(
                payload.Email, ProviderConsts.GoogleProvider, payload.Subject, ct);
            accessToken = tokenService.GenerateAccessToken(user);
            refreshToken = tokenService.GenerateRefreshToken();
            user.AddSession(refreshToken, TimeSpan.FromDays(options.Value.RefreshTokenLifetimeDays), request.DeviceName, request.IpAddress);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Response<GoogleLoginResponse>.Success(new GoogleLoginResponse(
            accessToken,
            refreshToken,
            DateTime.UtcNow.AddMinutes(options.Value.AccessTokenLifetimeMinutes),
            new GoogleLoginUser
            {
                Email = user.Credentials.Email,
                AvatarUrl = payload.Picture ?? string.Empty,
                Username = user.Username
            }));
    }
}
