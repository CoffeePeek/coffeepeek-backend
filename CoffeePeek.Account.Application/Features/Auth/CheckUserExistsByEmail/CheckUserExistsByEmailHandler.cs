using CoffeePeek.Account.Application.Common;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.Auth.CheckUserExistsByEmail;

public class CheckUserExistsByEmailRequestHandler
{
    public static async Task<Response<bool>> Handle(
        CheckUserExistsByEmailCommand command,
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var exists = await userRepository.UserExistsByEmail(command.Email, cancellationToken);
        return Response<bool>.Success(exists);
    }
}