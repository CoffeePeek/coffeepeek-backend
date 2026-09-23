using System.Net.Http.Headers;
using CoffeePeek.Shops.Infrastructure.Menu;
using FluentAssertions;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public class HttpMenuPhotoDownloaderTests
{
    [Fact]
    public async Task OversizedBody_WithoutContentLength_IsRejected()
    {
        var content = new StreamContent(new MemoryStream(new byte[HttpMenuPhotoDownloader.MaxPhotoBytes + 1]));
        content.Headers.ContentLength = null;

        (await HttpMenuPhotoDownloader.ReadCappedAsync(content, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SmallBody_IsReturned()
    {
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        (await HttpMenuPhotoDownloader.ReadCappedAsync(content, TestContext.Current.CancellationToken))
            .Should().Equal(1, 2, 3);
    }
}
