using System.Net;
using System.Net.Sockets;
using System.Text;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Exceptions;
using Minio;
using Minio.DataModel.Args;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public sealed class PrivatePhotoObjectReaderTimeoutTests
{
    [Fact]
    public async Task ConnectedStorageThatNeverSendsHeadersReturnsASanitizedUnavailableError()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        using var server = new LoopbackStorage();
        using var output = new MemoryStream();
        var copy = PrivatePhotoObjectReader.CopyAsync(server.Client, "private-bucket", "private-key", output, lifetime.Token);
        using var connection = await server.AcceptRequestAsync(lifetime.Token);
        var error = await Assert.ThrowsAsync<ServiceUnavailableException>(() => copy);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("X-Amz-", error.ToString());
        Assert.DoesNotContain("private-key", error.ToString());
        Assert.Empty(output.ToArray());
        Assert.False(lifetime.IsCancellationRequested);
    }

    [Fact]
    public async Task BodyStillStreamsAfterTheLocalHeaderDeadlineHasElapsed()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        using var server = new LoopbackStorage();
        using var output = new MemoryStream();
        var copy = PrivatePhotoObjectReader.CopyAsync(server.Client, "private-bucket", "private-key", output, lifetime.Token);
        using var connection = await server.AcceptRequestAsync(lifetime.Token);
        await HeadersAsync(connection, lifetime.Token);
        // A deadline mistakenly retained during CopyAsync would cancel this slow body at ten seconds.
        await Task.Delay(TimeSpan.FromSeconds(11), lifetime.Token);
        Assert.False(copy.IsCompleted);
        await connection.GetStream().WriteAsync(new byte[] { 1, 2, 3 }, lifetime.Token);
        await copy.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Fact]
    public async Task CallerCancellationStillStopsABodyAfterHeadersArrive()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        using var server = new LoopbackStorage();
        using var output = new MemoryStream();
        var copy = PrivatePhotoObjectReader.CopyAsync(server.Client, "private-bucket", "private-key", output, request.Token);
        using var connection = await server.AcceptRequestAsync(lifetime.Token);
        await HeadersAsync(connection, lifetime.Token);
        await request.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copy.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token));
        Assert.Empty(output.ToArray());
    }

    private static Task HeadersAsync(TcpClient connection, CancellationToken ct) => connection.GetStream().WriteAsync(
        Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/jpeg\r\nContent-Length: 3\r\nConnection: close\r\n\r\n"), ct).AsTask();

    private sealed class LoopbackStorage : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        public IMinioClient Client { get; }

        public LoopbackStorage()
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var client = new Mock<IMinioClient>();
            client.Setup(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()))
                .ReturnsAsync($"http://127.0.0.1:{port}/private-key?X-Amz-Signature=never-echo");
            Client = client.Object;
        }

        public async Task<TcpClient> AcceptRequestAsync(CancellationToken ct)
        {
            var connection = await listener.AcceptTcpClientAsync(ct);
            try
            {
                using var reader = new StreamReader(connection.GetStream(), Encoding.ASCII, false, 1024, leaveOpen: true);
                var remaining = 8192;
                string? line;
                do
                {
                    line = await reader.ReadLineAsync(ct);
                    Assert.NotNull(line);
                    remaining -= line.Length + 2;
                    Assert.True(remaining >= 0);
                } while (line.Length > 0);
                return connection;
            }
            catch { connection.Dispose(); throw; }
        }

        public void Dispose() => listener.Stop();
    }
}
