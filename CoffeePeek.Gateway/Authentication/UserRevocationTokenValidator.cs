using System.Security.Claims;
using CoffeePeek.Shared.Auth;

namespace CoffeePeek.Gateway.Authentication;

public sealed class UserRevocationTokenValidator(IRevokedUserStore revokedUserStore)
{
    public async Task<bool> IsAcceptedAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var subject = principal.FindFirstValue("sub")
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subject, out var userId))
            return false;

        return !await revokedUserStore.IsRevokedAsync(userId, cancellationToken);
    }
}
