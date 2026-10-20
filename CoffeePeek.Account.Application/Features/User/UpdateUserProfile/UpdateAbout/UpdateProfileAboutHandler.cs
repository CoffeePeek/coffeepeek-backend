using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateAbout;

public class UpdateProfileHandler
{
    public static async Task<UpdateEntityResponse<string>> Handle(
        UpdateProfileAboutCommand command,
        IUserRepository userRepository, 
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(command.UserId, ct);
        if (user == null)
        {
            throw new NotFoundException("User not found");
        }

        user.UpdateAbout(command.About);


        await unitOfWork.SaveChangesAsync(ct);
        
        return UpdateEntityResponse<string>.Success(user.About, "Profile updated successfully");
    }
}