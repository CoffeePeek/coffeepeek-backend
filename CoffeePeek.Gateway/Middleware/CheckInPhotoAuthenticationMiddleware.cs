using CoffeePeek.Shared.Auth;

namespace CoffeePeek.Gateway.Middleware;

/// <summary>Optional authentication must not turn a rejected credential into anonymous photo access.</summary>
public sealed class CheckInPhotoAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1/check-ins", out var remainder)
            && remainder.Value?.Contains("/photos/", StringComparison.OrdinalIgnoreCase) == true
            && context.Request.Headers.ContainsKey("Authorization")
            && (context.User.Identity?.IsAuthenticated != true
                || AccessTokenExpiry.Read(context.User) is not { } expires || expires <= DateTimeOffset.UtcNow))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }
        await next(context);
    }
}
