using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace CoffeePeek.Shared.Auth;

/// <summary>
/// Reads the caller identity from the authenticated principal only. Raw X-User-* headers are never
/// trusted here: <see cref="HeaderAuthenticationHandler"/> turns them into claims after verifying the gateway secret.
/// </summary>
public class HeaderUserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => GetUserId() is { } id && id != Guid.Empty;

    public Guid? GetUserId() =>
        Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    public Guid GetUserIdOrThrow()
    {
        var userId = GetUserId();
        return userId ?? throw new UnauthorizedAccessException("User ID is missing.");
    }

    public string? GetUsername() => User?.FindFirstValue(ClaimTypes.Name);

    public string GetUsernameOrThrow()
    {
        var username = GetUsername();
        if (string.IsNullOrEmpty(username))
            throw new UnauthorizedAccessException("Username is missing.");
        return username;
    }

    public string? GetUserRole()
    {
        var roles = User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        return roles is { Length: > 0 } ? string.Join(',', roles) : null;
    }
}
