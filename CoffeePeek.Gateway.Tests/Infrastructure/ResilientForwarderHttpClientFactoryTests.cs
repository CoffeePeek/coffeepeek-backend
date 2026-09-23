using System.Net;
using System.Net.Sockets;
using CoffeePeek.Gateway.Infrastructure;
using FluentAssertions;

namespace CoffeePeek.Gateway.Tests.Infrastructure;

public sealed class ResilientForwarderHttpClientFactoryTests
{
    [Fact]
    public async Task GetRequest_WhenConnectionIsTemporarilyRefused_RetriesAndReturnsResponse()
    {
        var transport = new StubTransport(failuresBeforeSuccess: 2);
        using var handler = ResilientForwarderHttpClientFactory.CreateHandler(
            transport,
            TimeSpan.Zero);
        using var client = new HttpMessageInvoker(handler);

        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://shops/api/Catalogs/equipments"),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        transport.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task PostRequest_WhenConnectionIsRefused_Retries()
    {
        // Connection-refused failures happen during the TCP connect phase, before any
        // request bytes are sent -- so retry is safe even for a non-idempotent method.
        var transport = new StubTransport(failuresBeforeSuccess: 1, SocketError.ConnectionRefused);
        using var handler = ResilientForwarderHttpClientFactory.CreateHandler(
            transport,
            TimeSpan.Zero);
        using var client = new HttpMessageInvoker(handler);

        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "http://shops/api/Catalogs"),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        transport.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task PutRequest_WhenConnectFailsWithResourceTemporarilyUnavailable_RetriesAndReturnsResponse()
    {
        // Reproduces Sentry issue 143086407: PUT /api/tokens failing with
        // SocketError.TryAgain ("Resource temporarily unavailable") while the Gateway
        // was connecting to account:8080 during a backend container restart.
        var transport = new StubTransport(failuresBeforeSuccess: 2, SocketError.TryAgain);
        using var handler = ResilientForwarderHttpClientFactory.CreateHandler(
            transport,
            TimeSpan.Zero);
        using var client = new HttpMessageInvoker(handler);

        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, "http://account/api/tokens"),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        transport.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task PostRequest_WhenConnectionIsReset_DoesNotRetry()
    {
        // ConnectionReset can occur on an already-established connection, i.e. after
        // request bytes may already have been sent -- retrying a non-idempotent request
        // in that case could cause it to execute twice, so this must NOT be retried.
        var transport = new StubTransport(failuresBeforeSuccess: 1, SocketError.ConnectionReset);
        using var handler = ResilientForwarderHttpClientFactory.CreateHandler(
            transport,
            TimeSpan.Zero);
        using var client = new HttpMessageInvoker(handler);

        var act = () => client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "http://shops/api/Catalogs"),
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        transport.Attempts.Should().Be(1);
    }

    private sealed class StubTransport(int failuresBeforeSuccess, SocketError socketError = SocketError.ConnectionRefused)
        : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;

            if (Attempts <= failuresBeforeSuccess)
            {
                return Task.FromException<HttpResponseMessage>(new HttpRequestException(
                    "Resource temporarily unavailable (shops:8080)",
                    new SocketException((int)socketError)));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
