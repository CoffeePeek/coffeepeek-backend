using CoffeePeek.Gateway.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Gateway.Tests.Middleware;

public class GatewayRequestLoggingMiddlewareTests
{
    [Theory]
    [InlineData(StatusCodes.Status200OK, LogLevel.Information)]
    [InlineData(StatusCodes.Status401Unauthorized, LogLevel.Information)]
    [InlineData(StatusCodes.Status400BadRequest, LogLevel.Warning)]
    [InlineData(StatusCodes.Status500InternalServerError, LogLevel.Error)]
    public async Task InvokeAsync_LogsResponseAtExpectedLevel(int statusCode, LogLevel expectedLevel)
    {
        var logger = new RecordingLogger();
        var middleware = new GatewayRequestLoggingMiddleware(context =>
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }, logger);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/tokens";

        await middleware.InvokeAsync(context);

        logger.Levels.Should().ContainSingle().Which.Should().Be(expectedLevel);
    }

    private sealed class RecordingLogger : ILogger<GatewayRequestLoggingMiddleware>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
