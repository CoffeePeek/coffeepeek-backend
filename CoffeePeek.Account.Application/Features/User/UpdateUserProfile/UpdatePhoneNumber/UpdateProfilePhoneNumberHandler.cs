using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdatePhoneNumber;

public class UpdatePhoneNumberHandler
{
    public static async Task<UpdateEntityResponse<string>> Handle(
        UpdateProfilePhoneNumberCommand request, 
        IUserRepository userRepository, 
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(request.UserId, ct);
        if (user == null)
        {
            throw new NotFoundException("User not found");
        }

        var phoneNumber = PhoneNumber.Create(request.PhoneNumber);
        user.UpdatePhoneNumber(phoneNumber);


        await unitOfWork.SaveChangesAsync(ct);

        return UpdateEntityResponse<string>.Success(phoneNumber.ToString(), "Phone number updated successful");
    }
}