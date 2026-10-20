using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateEmail;

public class UpdateEmailRequestHandler
{
    public static async Task<(UpdateEntityResponse<string>, UserRegisteredInternalEvent)> Handle(
        UpdateProfileEmailCommand request,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(request.UserId, ct)
                   ?? throw new NotFoundException("User not found");

        var existingOwner = await userRepository.GetByEmail(request.Email, ct);
        if (existingOwner is not null && existingOwner.Id != request.UserId)
            throw new DomainException("Email is already taken");

        // UpdateEmail internally calls ResetEmailConfirmedFlow():
        // sets EmailConfirmed=false and generates a new EmailConfirmationToken
        user.Credentials.UpdateEmail(request.Email);

        await unitOfWork.SaveChangesAsync(ct);

        var confirmationEvent = UserRegisteredInternalEvent.For(user);

        return (
            UpdateEntityResponse<string>.Success(user.Credentials.Email, "Email updated. A confirmation link has been sent to your new address."),
            confirmationEvent
        );
    }
}