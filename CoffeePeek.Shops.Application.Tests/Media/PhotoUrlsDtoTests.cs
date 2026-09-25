using System.Linq;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using FluentAssertions;

namespace CoffeePeek.Shops.Application.Tests.Media;

public class PhotoUrlsDtoTests
{
    private const string PublicEndpoint = "https://media.coffeepeek.by";
    private const string ProxyEndpoint = "https://media.coffeepeek.by/img";
    private const string Bucket = "coffeepeek.shops";
    private const string Key = "shops/abc.jpg";

    [Fact]
    public void Create_WithImageProxy_ReturnsFourDistinctPresetUrls()
    {
        var urls = PhotoUrlsDto.Create(PublicEndpoint, ProxyEndpoint, Bucket, Key);

        urls.Should().NotBeNull();
        urls.Thumbnail.Should().Be("https://media.coffeepeek.by/img/_/thumb/plain/coffeepeek.shops/shops/abc.jpg");
        urls.Card.Should().Be("https://media.coffeepeek.by/img/_/card/plain/coffeepeek.shops/shops/abc.jpg");
        urls.Detail.Should().Be("https://media.coffeepeek.by/img/_/detail/plain/coffeepeek.shops/shops/abc.jpg");
        urls.Fullscreen.Should().Be("https://media.coffeepeek.by/img/_/full/plain/coffeepeek.shops/shops/abc.jpg");
        new[] { urls.Thumbnail, urls.Card, urls.Detail, urls.Fullscreen }.Distinct().Should().HaveCount(4);
    }

    [Fact]
    public void Create_WithImageProxy_UsesSharedPresetConstants()
    {
        var urls = PhotoUrlsDto.Create(PublicEndpoint, ProxyEndpoint, Bucket, Key);

        urls.Thumbnail.Should().Be(MediaStorageUrlBuilder.BuildVariantUrl(ProxyEndpoint, Bucket, Key, ImageVariantPresets.Thumbnail));
        urls.Card.Should().Be(MediaStorageUrlBuilder.BuildVariantUrl(ProxyEndpoint, Bucket, Key, ImageVariantPresets.Card));
        urls.Detail.Should().Be(MediaStorageUrlBuilder.BuildVariantUrl(ProxyEndpoint, Bucket, Key, ImageVariantPresets.Detail));
        urls.Fullscreen.Should().Be(MediaStorageUrlBuilder.BuildVariantUrl(ProxyEndpoint, Bucket, Key, ImageVariantPresets.Fullscreen));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutImageProxy_AllVariantsFallBackToPublicUrl(string imageProxyEndpoint)
    {
        var expected = MediaStorageUrlBuilder.BuildPublicUrl(PublicEndpoint, Bucket, Key);

        var urls = PhotoUrlsDto.Create(PublicEndpoint, imageProxyEndpoint, Bucket, Key);

        expected.Should().Be("https://media.coffeepeek.by/coffeepeek.shops/shops/abc.jpg");
        urls.Should().NotBeNull();
        urls.Thumbnail.Should().Be(expected);
        urls.Card.Should().Be(expected);
        urls.Detail.Should().Be(expected);
        urls.Fullscreen.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_MissingStorageKey_ReturnsNull(string storageKey)
    {
        PhotoUrlsDto.Create(PublicEndpoint, ProxyEndpoint, Bucket, storageKey).Should().BeNull();
        PhotoUrlsDto.Create(PublicEndpoint, null, Bucket, storageKey).Should().BeNull();
    }

    [Fact]
    public void Create_BothEndpointsEmpty_ReturnsNull()
    {
        PhotoUrlsDto.Create("", "", Bucket, Key).Should().BeNull();
        PhotoUrlsDto.Create(null, null, Bucket, Key).Should().BeNull();
    }

    [Theory]
    [InlineData(ProxyEndpoint)]
    [InlineData("")]
    public void Create_OptionsOverload_MatchesStringOverload(string imageProxyEndpoint)
    {
        var options = new MediaPublicUrlOptions
        {
            PublicEndpoint = PublicEndpoint,
            ImageProxyEndpoint = imageProxyEndpoint,
            ShopBucketName = Bucket
        };

        var fromOptions = PhotoUrlsDto.Create(options, options.ShopBucketName, Key);
        var fromStrings = PhotoUrlsDto.Create(PublicEndpoint, imageProxyEndpoint, Bucket, Key);

        fromOptions.Should().BeEquivalentTo(fromStrings);
    }
}
