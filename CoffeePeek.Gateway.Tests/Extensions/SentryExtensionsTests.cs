using System.Net.Sockets;
using CoffeePeek.Gateway.Extensions;
using FluentAssertions;

namespace CoffeePeek.Gateway.Tests.Extensions;

public class SentryExtensionsTests
{
    private static readonly SentryHint Hint = new();

    [Fact]
    public void FilterExpectedProxyDisconnects_DropsHttpForwarderConnectionReset()
    {
        var sentryEvent = new SentryEvent(
            new IOException("Unable to read data from the transport connection: Connection reset by peer.",
                new SocketException((int)SocketError.ConnectionReset)))
        {
            Logger = "Yarp.ReverseProxy.Forwarder.HttpForwarder"
        };

        var result = SentryExtensions.FilterExpectedProxyDisconnects(sentryEvent, Hint);

        result.Should().BeNull();
    }

    [Fact]
    public void FilterExpectedProxyDisconnects_KeepsHttpForwarderErrorsThatAreNotAResetSocket()
    {
        var sentryEvent = new SentryEvent(new HttpRequestException("Connection refused"))
        {
            Logger = "Yarp.ReverseProxy.Forwarder.HttpForwarder"
        };

        var result = SentryExtensions.FilterExpectedProxyDisconnects(sentryEvent, Hint);

        result.Should().BeSameAs(sentryEvent);
    }

    [Fact]
    public void FilterExpectedProxyDisconnects_KeepsConnectionResetFromOtherLoggers()
    {
        var sentryEvent = new SentryEvent(
            new IOException("reset", new SocketException((int)SocketError.ConnectionReset)))
        {
            Logger = "Some.Other.Logger"
        };

        var result = SentryExtensions.FilterExpectedProxyDisconnects(sentryEvent, Hint);

        result.Should().BeSameAs(sentryEvent);
    }
}
