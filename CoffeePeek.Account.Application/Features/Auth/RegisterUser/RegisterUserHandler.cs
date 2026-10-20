using CoffeePeek.Account.Application.Common;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.Auth.RegisterUser;

public static class RegisterUserHandler
{
    public static async Task<(CreateEntityResponse, UserRegisteredInternalEvent)> Handle(
        RegisterUserCommand request,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasherService passwordHasher,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (await userRepository.UserExistsByEmail(request.Email, ct))
            throw new DomainException("Email already exists");

        if (!await userRepository.IsUsernameUnique(request.UserName, ct))
            throw new DomainException("Username already exists");

        UserCredential.EnsureValidNewPassword(request.Password);

        var passwordHash = passwordHasher.HashPassword(request.Password);
        var defaultRole = await roleRepository.GetRoleAsync(RoleConsts.User)
                          ?? throw new DomainException("Default role not found");

        var user = Domain.Entities.UserAggregate.User.Register(
            request.Email,
            request.UserName,
            passwordHash,
            defaultRole);

        userRepository.Add(user, ct);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            // Race condition: two concurrent registrations with the same email.
            throw new DomainException("Email already exists");
        }

        var registeredEvent = UserRegisteredInternalEvent.For(user);

        return (CreateEntityResponse.Success(), registeredEvent);
    }
}