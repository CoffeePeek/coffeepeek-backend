using System.Net.Sockets;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;
using Yarp.ReverseProxy.Forwarder;

namespace CoffeePeek.Gateway.Infrastructure;

/// <summary>
/// Adds narrowly scoped transport retries to YARP's own HTTP transport.
/// </summary>
internal sealed class ResilientForwarderHttpClientFactory : ForwarderHttpClientFactory
{
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(500);

    protected override HttpMessageHandler WrapHandler(
        ForwarderHttpClientContext context,
        HttpMessageHandler handler)
    {
        var innerHandler = base.WrapHandler(context, handler);
        return CreateHandler(innerHandler, DefaultRetryDelay);
    }

    internal static HttpMessageHandler CreateHandler(
        HttpMessageHandler innerHandler,
        TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(innerHandler);

        var retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>(IsTransientConnectFailure),
                MaxRetryAttempts = 3,
                Delay = retryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = retryDelay > TimeSpan.Zero
            })
            .Build();

        // Retry-safety here is a property of the failure *phase*, not the HTTP method:
        // IsTransientConnectFailure only matches socket errors that can occur while the
        // TCP connection is still being established (see below), so no request bytes can
        // have reached the server yet. That makes retry safe even for non-idempotent
        // methods (POST/PUT/PATCH/DELETE), so every request goes through the same pipeline.
        return new ResilienceHandler(_ => retryPipeline)
        {
            InnerHandler = innerHandler
        };
    }

    /// <summary>
    /// Matches transport failures that can only occur while the TCP connection is being
    /// established (before any request bytes are sent): the destination actively refused
    /// the connection, or a transient local/DNS resource issue prevented the connect
    /// attempt from completing (EAGAIN / "Resource temporarily unavailable" -- commonly seen
    /// when a downstream container is mid-restart). Mid-stream failures on an already
    /// established connection (e.g. SocketError.ConnectionReset) are intentionally NOT
    /// matched here, since retrying those could re-execute a non-idempotent request that the
    /// server already received.
    /// </summary>
    private static bool IsTransientConnectFailure(HttpRequestException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.ConnectionRefused or SocketError.TryAgain })
            {
                return true;
            }
        }

        return false;
    }
}
