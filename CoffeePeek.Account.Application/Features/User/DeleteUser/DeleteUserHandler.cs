using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Domain.Interfaces.Persistance;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Application.Features.User.DeleteUser;

public static class DeleteUserHandler
{
    public static async Task<(Response<AccountDeletionRequestResponse>, AccountDeletionRequestedInternalEvent?)> Handle(
        DeleteUserCommand command,
        IUserRepository userRepository,
        IAccountDeletionRequestRepository deletionRequestRepository,
        IAccountDeletionTokenService tokenService,
        IOptions<AccountDeletionOptions> options,
        TimeProvider timeProvider,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(command.UserId, ct);

        if (user == null)
            return (Response<AccountDeletionRequestResponse>.Error(
                System.Net.HttpStatusCode.NotFound,
                "User not found"), null);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var settings = options.Value;
        var tokenLifetime = TimeSpan.FromHours(settings.TokenLifetimeHours);
        var resendCooldown = TimeSpan.FromMinutes(settings.ResendCooldownMinutes);
        var request = await deletionRequestRepository.GetActiveByUserId(command.UserId, ct);

        if (request is { Status: AccountDeletionRequestStatus.Processing })
            return (Response<AccountDeletionRequestResponse>.Success(AccountDeletionRequestResponse.From(request)), null);

        if (request is not null && !request.CanResend(nowUtc, settings.MaxEmailsPerHour))
            return (Response<AccountDeletionRequestResponse>.Success(AccountDeletionRequestResponse.From(request)), null);

        var (token, tokenHash) = tokenService.Generate();

        if (request is null)
        {
            request = AccountDeletionRequest.Create(
                command.UserId,
                tokenHash,
                nowUtc,
                tokenLifetime,
                resendCooldown);
            await deletionRequestRepository.Add(request, ct);
        }
        else
        {
            request.Resend(tokenHash, nowUtc, tokenLifetime, resendCooldown, settings.MaxEmailsPerHour);
        }

        await unitOfWork.SaveChangesAsync(ct);

        var @event = new AccountDeletionRequestedInternalEvent(
            request.Id,
            user.Id,
            user.Credentials.Email.Value,
            user.Username.Value,
            token);

        return (Response<AccountDeletionRequestResponse>.Success(AccountDeletionRequestResponse.From(request)), @event);
    }
}