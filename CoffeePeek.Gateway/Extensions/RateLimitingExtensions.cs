using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace CoffeePeek.Gateway.Extensions;

public static class RateLimitingExtensions
{
    // Policy names — used in route metadata and middleware
    public const string GlobalPolicy = "global";
    public const string AuthEndpointsPolicy = "auth-endpoints";
    public const string AccountDeletionPolicy = "account-deletion";
    public const string MediaUploadPolicy = "media-upload";
    public const string ModerationSubmissionPolicy = "moderation-submission";

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Every named policy is partitioned per client: an unpartitioned limiter is one bucket shared by all users.
            options.AddPolicy(GlobalPolicy, ctx => PerClient(ctx, TimeSpan.FromMinutes(1), 6, 200));

            // Auth endpoints: brute-force protection
            options.AddPolicy(AuthEndpointsPolicy, ctx => PerClient(ctx, TimeSpan.FromMinutes(1), 6, 20));

            // Capability-token and authenticated account deletion: limits confirmation-token probing and email abuse
            options.AddPolicy(AccountDeletionPolicy, ctx => PerClient(ctx, TimeSpan.FromHours(1), 12, 10));

            options.AddPolicy(MediaUploadPolicy, ctx => PerClient(ctx, TimeSpan.FromMinutes(1), 6, 10));

            // Only submissions are throttled; moderator reads on the same routes must not be starved.
            options.AddPolicy(ModerationSubmissionPolicy, ctx =>
                HttpMethods.IsGet(ctx.Request.Method)
                    ? RateLimitPartition.GetNoLimiter("unthrottled-read")
                    : PerClient(ctx, TimeSpan.FromMinutes(1), 6, 15));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                PerClient(ctx, TimeSpan.FromMinutes(1), 6, 300));
        });

        return services;
    }

    /// <summary>
    /// Client IP after trusted forwarded-header processing, shared with YARP's outgoing X-Forwarded-For.
    /// </summary>
    public static string GetClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> PerClient(HttpContext context, TimeSpan window, int segments, int permits) =>
        RateLimitPartition.GetSlidingWindowLimiter(GetClientKey(context), _ => new SlidingWindowRateLimiterOptions
        {
            Window = window,
            SegmentsPerWindow = segments,
            PermitLimit = permits,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
}
