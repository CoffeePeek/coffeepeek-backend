using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Contract.Events.Account;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateUsername;

public static class UpdateUsernameHandler
{
    public static async Task<(UpdateEntityResponse<string>, UserNameChangedEvent)> Handle(
        UpdateProfileUsernameCommand command,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(command.UserId, ct)
                   ?? throw new NotFoundException("User not found");

        var userName = Username.Create(command.Username);
        user.UpdateUsername(userName);

        await unitOfWork.SaveChangesAsync(ct);

        var @event = new UserNameChangedEvent
        {
            UserId = user.Id,
            NewUserName = userName.Value,
            ChangedAt = DateTime.UtcNow
        };

        return (
            UpdateEntityResponse<string>.Success(userName.ToString(), "Username updated successful"),
            @event
        );
    }
}