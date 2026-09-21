using System.Net;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Contract.Events.Account;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.User.DeleteUser;

public static class ConfirmAccountDeletionHandler
{
    public static async Task<(Response<AccountDeletionRequestResponse>, AccountDeletionRequestedV1?)> Handle(
        ConfirmAccountDeletionCommand command,
        IAccountDeletionRequestRepository deletionRequestRepository,
        IAccountDeletionTokenService tokenService,
        IUserRepository userRepository,
        IRevokedUserStore revokedUserStore,
        ISessionTerminationNotifier sessionTerminationNotifier,
        IOptions<JWTOptions> jwtOptions,
        TimeProvider timeProvider,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return (Response<AccountDeletionRequestResponse>.Error(
                HttpStatusCode.BadRequest,
                "Confirmation token is required."), null);

        var tokenHash = tokenService.Hash(command.Token);
        var request = await deletionRequestRepository.GetByTokenHash(tokenHash, ct);

        if (request is null)
            return (Response<AccountDeletionRequestResponse>.Error(
                HttpStatusCode.NotFound,
                "Account deletion request not found."), null);

        if (request.Status == AccountDeletionRequestStatus.Cancelled)
            return (Response<AccountDeletionRequestResponse>.Error(
                HttpStatusCode.Conflict,
                "Account deletion request was cancelled."), null);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (request.Status == AccountDeletionRequestStatus.PendingConfirmation &&
            nowUtc > request.ExpiresAtUtc)
            return (Response<AccountDeletionRequestResponse>.Error(
                HttpStatusCode.Gone,
                "Deletion confirmation token has expired."), null);

        var accessTokenTtl = TimeSpan.FromMinutes(jwtOptions.Value.AccessTokenLifetimeMinutes);

        if (request.Status != AccountDeletionRequestStatus.PendingConfirmation)
        {
            await EnsureRevokedAsync(revokedUserStore, request.UserId, accessTokenTtl, ct);
            return (Response<AccountDeletionRequestResponse>.Success(ToResponse(request)), null);
        }

        AccountDeletionRequest.ValidateReason(command.Reason, command.OtherReason);

        var user = await userRepository.GetById(request.UserId, ct);
        if (user is null)
            return (Response<AccountDeletionRequestResponse>.Error(
                HttpStatusCode.NotFound,
                "User not found."), null);

        request.Confirm(command.Reason, command.OtherReason, nowUtc);
        user.SetSoftDelete();
        user.RevokeAllSessions();
        await deletionRequestRepository.Update(request, ct);
        await userRepository.Update(user, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await EnsureRevokedAsync(revokedUserStore, user.Id, accessTokenTtl, ct);
        await sessionTerminationNotifier.NotifyForceLogoutAsync(
            user.Id,
            SessionTerminationReasons.UserDeleted,
            ct);

        var @event = new AccountDeletionRequestedV1(
            request.Id,
            request.UserId,
            request.RequestedAtUtc);

        return (Response<AccountDeletionRequestResponse>.Success(ToResponse(request)), @event);
    }

    private static async Task EnsureRevokedAsync(
        IRevokedUserStore revokedUserStore,
        Guid userId,
        TimeSpan ttl,
        CancellationToken ct)
    {
        if (!await revokedUserStore.RevokeAsync(userId, ttl, ct))
            throw new InvalidOperationException("Could not persist access-token revocation.");
    }

    private static AccountDeletionRequestResponse ToResponse(AccountDeletionRequest request) =>
        new(request.Id, request.Status, request.ExpiresAtUtc, request.ResendAvailableAtUtc);
}
