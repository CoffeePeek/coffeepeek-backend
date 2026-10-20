using System.Net;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.Auth.AppleLogin;

public static class AppleLoginHandler
{
    public static async Task<Response<AppleLoginResponse>> Handle(
        AppleLoginCommand request,
        IAppleLoginChallengeStore challenges,
        IAppleAuthService appleAuth,
        IUserRepository users,
        IExternalAuthService externalAuth,
        IJWTTokenService tokens,
        IOptions<JWTOptions> options,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var nonce = await challenges.GetNonceAsync(request.ChallengeId, ct);
        if (nonce is null)
            return InvalidLogin();
        var identity = await appleAuth.ValidateIdTokenAsync(request.IdToken, nonce, ct);
        if (identity is null || !await challenges.ConsumeAsync(request.ChallengeId, nonce, ct))
            return InvalidLogin();

        // Apple may omit email on later authorizations. The stable subject is the identity.
        var user = await users.GetByProvider(ProviderConsts.AppleProvider, identity.Subject, ct);
        if (user is null)
        {
            if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
                return Response<AppleLoginResponse>.Error(HttpStatusCode.BadRequest,
                    "Apple did not provide a verified email. Allow email sharing and try again.");
            // Existing accounts with the same email are never silently linked.
            user = await externalAuth.GetOrCreate(identity.Email, ProviderConsts.AppleProvider, identity.Subject, ct);
        }

        var session = AuthSession.Issue(user, tokens, options.Value, request.DeviceName, request.IpAddress);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            // A concurrent first login may have created the same Apple account.
            user = await users.GetByProvider(ProviderConsts.AppleProvider, identity.Subject, ct);
            if (user is null) throw;
            session = AuthSession.Issue(user, tokens, options.Value, request.DeviceName, request.IpAddress);
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Response<AppleLoginResponse>.Success(new(session.AccessToken, session.RefreshToken, session.AccessTokenExpiresAt));
    }

    private static Response<AppleLoginResponse> InvalidLogin() =>
        Response<AppleLoginResponse>.Error(HttpStatusCode.Unauthorized, "Apple login expired or is invalid. Please try again.");
}
