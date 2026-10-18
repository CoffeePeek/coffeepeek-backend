namespace CoffeePeek.Shared.Auth.Constants;

/// <summary>
/// HTTP header names used by the API Gateway to propagate authenticated user identity
/// to downstream microservices. These headers are set exclusively by the Gateway after
/// JWT validation — downstream services must never trust these headers from external clients.
/// </summary>
public static class GatewayHeaderConsts
{
    /// <summary>The authenticated user's unique identifier (GUID).</summary>
    public const string XUserId = "X-User-Id";

    /// <summary>The authenticated user's username.</summary>
    public const string XUserName = "X-User-Name";

    /// <summary>The authenticated user's role(s), comma-separated when multiple.</summary>
    public const string XUserRole = "X-User-Role";

    /// <summary>The authenticated user's email address.</summary>
    public const string XUserEmail = "X-User-Email";

    /// <summary>Validated JWT expiration as Unix seconds; never a client-supplied cache deadline.</summary>
    public const string XUserTokenExpiresAt = "X-User-Token-Expires-At";

    /// <summary>Shared secret proving the request was forwarded by the API Gateway.</summary>
    public const string XGatewayAuth = "X-Gateway-Auth";
}
