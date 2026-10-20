using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Application.Features.Auth.OAuthLogin;

using User = Domain.Entities.UserAggregate.User;

public class ExternalAuthService(
    IUserRepository userRepository,
    IRoleRepository roleRepository) : IExternalAuthService
{
    private const int UsernameSuffixLength = 4;
    private const int MaxUsernameLength = 30;
    private const int UniqueUsernameAttempts = 5;

    public async Task<User> GetOrCreate(string email, string provider, string providerId,
        CancellationToken ct)
    {
        // H-7/H-8: find by (provider + providerId) first — exact match
        var userByProvider = await userRepository.GetByProvider(provider, providerId, ct);
        if (userByProvider is not null)
            return userByProvider;

        // H-9: email belongs to an existing account that was NOT created via this provider.
        // Silently linking would allow account takeover — require explicit account-linking flow instead.
        var userByEmail = await userRepository.GetByEmail(email, ct);
        if (userByEmail is not null)
            throw new UnauthorizedException(
                "An account with this email already exists. Please log in with your email and password and link the external provider from your profile settings.");

        var newUser = User.CreateExternal(email, provider, providerId);
        if (!await userRepository.IsUsernameUnique(newUser.Username, ct))
            newUser = await CreateWithUniqueUsername(email, provider, providerId, newUser.Username, ct);

        var role = await roleRepository.GetRoleAsync(RoleConsts.User)
                   ?? throw new ApplicationException($"Role {RoleConsts.User} not found");

        newUser.AssignRole(role);
        userRepository.Add(newUser, ct);
        return newUser;
    }

    private async Task<User> CreateWithUniqueUsername(
        string email,
        string provider,
        string providerId,
        string baseSanitizedName,
        CancellationToken ct)
    {
        var baseName = TrimForSuffix(baseSanitizedName);

        for (var attempt = 0; attempt < UniqueUsernameAttempts; attempt++)
        {
            var suffix = Guid.NewGuid().ToString("N")[..UsernameSuffixLength];
            var candidate = baseName + suffix;
            if (await userRepository.IsUsernameUnique(candidate, ct))
                return User.CreateExternal(email, provider, providerId, candidate);
        }

        throw new DomainException("Could not allocate a unique username");
    }

    private static string TrimForSuffix(string baseSanitizedName)
    {
        var maxBaseLength = MaxUsernameLength - UsernameSuffixLength;
        var baseName = baseSanitizedName.Length > maxBaseLength
            ? baseSanitizedName[..maxBaseLength]
            : baseSanitizedName;

        if (baseName.Length == 0 || !char.IsLetter(baseName[0]))
            baseName = "user" + baseName;

        if (baseName.Length < 3)
            baseName = baseName.PadRight(3, '0');

        if (baseName.Length > maxBaseLength)
            baseName = baseName[..maxBaseLength];

        return baseName;
    }
}
