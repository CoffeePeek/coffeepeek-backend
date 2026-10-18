using CoffeePeek.Gateway.Extensions;
using CoffeePeek.Gateway.Middleware;
using CoffeePeek.Shared.Web.Extensions;
using CoffeePeek.Shared.Web.Handlers;
using CoffeePeek.Shared.Web.Logging;
using CoffeePeek.Shared.Web.Sentry;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffePeek.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure ────────────────────────────────────────────────────────────

// Kestrel request body size limits (DoS protection).
// Media upload routes are handled by the media service itself.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize    = 10 * 1024 * 1024; // 10 MB
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;   // 32 KB
    options.Limits.MaxRequestLineSize    = 8 * 1024;         //  8 KB
});

builder.AddServiceDefaults();
builder.Services.AddServiceDiscovery();
builder.AddSerilogLogging();
builder.ConfigureEnvironment();
builder.WebHost.UseCoffeePeekSentry(options => options.SetBeforeSend(SentryExtensions.FilterExpectedProxyDisconnects));

// ── API Documentation ─────────────────────────────────────────────────────────
builder.Services.AddGatewayOpenApi();

// ── Reverse Proxy ─────────────────────────────────────────────────────────────
// Routes and clusters are loaded from appsettings.json "ReverseProxy" section.
// To add a new service or route, edit appsettings.json — no code change required.
builder.Services.AddGatewayProxy(builder.Configuration);
builder.Services.AddTrustedProxyForwarding(builder.Configuration);

// ── Security ──────────────────────────────────────────────────────────────────
builder.Services.AddGatewayAuth(builder.Environment, builder.Configuration);
builder.Services.AddUserRevocationStore();
builder.Services.AddCorsModule(builder.Environment);
builder.Services.AddGatewayRateLimiting();

// ── Cross-cutting ─────────────────────────────────────────────────────────────
builder.Services.AddResponseCaching();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ═════════════════════════════════════════════════════════════════════════════
var app = builder.Build();
// ═════════════════════════════════════════════════════════════════════════════
app.UseForwardedHeaders();

// ── API Documentation ─────────────────────────────────────────────────────────
app.UseGatewayScalarUi();

// ── Observability ─────────────────────────────────────────────────────────────
app.MapDefaultEndpoints();

// ── Middleware pipeline (order matters) ───────────────────────────────────────
app.UseExceptionHandler();
app.UseCors();

// Rate limiting before auth — rejected requests don't waste auth processing.
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<CheckInPhotoAuthenticationMiddleware>();
app.UseAuthorization();

app.UseResponseCaching();

// Gateway-level structured logging: route, cluster, status code, duration, correlation ID.
app.UseMiddleware<GatewayRequestLoggingMiddleware>();

// ── Endpoints ─────────────────────────────────────────────────────────────────

// Gateway self health check (separate from downstream service health checks).
app.MapGet("/health/gateway", () => Results.Ok(new
    {
        status = "healthy",
        service = "Gateway",
        timestamp = DateTime.UtcNow
    }))
    .WithName("GatewayHealthCheck")
    .WithTags("Health");

app.MapReverseProxy();

app.Run();
