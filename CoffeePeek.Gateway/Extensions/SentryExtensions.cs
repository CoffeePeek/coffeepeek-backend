using System.Net.Sockets;
using Sentry;

namespace CoffeePeek.Gateway.Extensions;

public static class SentryExtensions
{
    /// <summary>
    /// Drops "connection reset by peer" noise from proxied upgraded (WebSocket) connections, e.g. the
    /// SignalR realtime session. Clients close tabs, lose network, or get backgrounded constantly, and
    /// an abrupt TCP reset from either side isn't something the Gateway or the origin can prevent.
    /// </summary>
    public static SentryEvent? FilterExpectedProxyDisconnects(SentryEvent sentryEvent, SentryHint hint)
    {
        if (sentryEvent.Logger == "Yarp.ReverseProxy.Forwarder.HttpForwarder" && HasConnectionReset(sentryEvent.Exception))
            return null;

        return sentryEvent;
    }

    private static bool HasConnectionReset(Exception? exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is SocketException { SocketErrorCode: SocketError.ConnectionReset })
                return true;
        }

        return false;
    }
}
