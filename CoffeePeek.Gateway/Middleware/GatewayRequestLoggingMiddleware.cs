using System.Diagnostics;
using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Shared.Web.Logging;
using CoffeePeek.Shared.Web.Sentry;
using Yarp.ReverseProxy.Model;

namespace CoffeePeek.Gateway.Middleware;

/// <summary>
/// Middleware that logs every proxied request with:
/// - Correlation ID
/// - HTTP method and path
/// - Matched YARP route ID and cluster ID
/// - Response status code
/// - Elapsed time in milliseconds
/// Also emits Sentry metrics for popular-request tracking.
/// </summary>
public class GatewayRequestLoggingMiddleware(RequestDelegate next, ILogger<GatewayRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (RequestLoggingExtensions.IsHealthCheckPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        await next(context);

        stopwatch.Stop();

        // Correlation ID is set by CorrelationIdTransformProvider during proxying.
        // For non-proxied requests (health checks, Scalar UI) it falls back to TraceIdentifier.
        var correlationId = context.Items.TryGetValue(CorrelationIdTransformProvider.CorrelationIdHeader, out var cid)
            ? cid as string
            : context.TraceIdentifier;

        // YARP sets IReverseProxyFeature on the HttpContext after routing
        // For non-proxied requests (health checks, Scalar UI), the feature won't be available
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeId = proxyFeature?.Route.Config.RouteId ?? "n/a";
        var clusterId = proxyFeature?.Cluster?.Config.ClusterId ?? "n/a";

        var statusCode = context.Response.StatusCode;
        var method = context.Request.Method;
        var path = context.Request.Path;
        var elapsed = stopwatch.ElapsedMilliseconds;

        var metricRoute = routeId != "n/a"
            ? routeId
            : SentryRequestMetrics.NormalizePath(path.Value ?? "/");
        SentryRequestMetrics.Record(context, elapsed, metricRoute);

        if (statusCode >= 500)
        {
            logger.LogError(
                "Gateway [{CorrelationId}] {Method} {Path} → {StatusCode} ({Elapsed}ms) | route={RouteId} cluster={ClusterId}",
                correlationId, method, path, statusCode, elapsed, routeId, clusterId);
        }
        else if (statusCode >= 400 && statusCode != StatusCodes.Status401Unauthorized)
        {
            logger.LogWarning(
                "Gateway [{CorrelationId}] {Method} {Path} → {StatusCode} ({Elapsed}ms) | route={RouteId} cluster={ClusterId}",
                correlationId, method, path, statusCode, elapsed, routeId, clusterId);
        }
        else
        {
            logger.LogInformation(
                "Gateway [{CorrelationId}] {Method} {Path} → {StatusCode} ({Elapsed}ms) | route={RouteId} cluster={ClusterId}",
                correlationId, method, path, statusCode, elapsed, routeId, clusterId);
        }
    }
}
