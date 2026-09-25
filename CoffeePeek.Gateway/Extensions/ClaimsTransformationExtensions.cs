using System.Security.Claims;
using CoffeePeek.Shared.Auth.Constants;

namespace CoffeePeek.Gateway.Extensions;

/// <summary>
/// Utility methods for extracting JWT claims and mapping them to the
/// <see cref="GatewayHeaderConsts"/> headers that are forwarded to downstream services.
/// </summary>
public static class ClaimsTransformationExtensions
{
    /// <summary>
    /// Extracts the authenticated user's claims from <paramref name="user"/> and returns
    /// them as a dictionary keyed by the canonical <see cref="GatewayHeaderConsts"/> header names.
    /// Returns an empty dictionary when the user is not authenticated.
    /// </summary>
    public static Dictionary<string, string> ExtractClaimsAsHeaders(ClaimsPrincipal user)
    {
        var headers = new Dictionary<string, string>();

        if (!user.Identity?.IsAuthenticated ?? true)
            return headers;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            headers[GatewayHeaderConsts.XUserId] = userId;

        var userName = user.FindFirst(ClaimTypes.Name)?.Value
                       ?? user.FindFirst("preferred_username")?.Value;
        if (!string.IsNullOrEmpty(userName))
            headers[GatewayHeaderConsts.XUserName] = Uri.EscapeDataString(userName);

        var email = user.FindFirst(ClaimTypes.Email)?.Value;
        if (!string.IsNullOrEmpty(email))
            headers[GatewayHeaderConsts.XUserEmail] = email;

        var roles = user.FindAll(ClaimTypes.Role)
            .Select(r => r.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .ToArray();
        if (roles.Length > 0)
            headers[GatewayHeaderConsts.XUserRole] = string.Join(",", roles);

        return headers;
    }
}
