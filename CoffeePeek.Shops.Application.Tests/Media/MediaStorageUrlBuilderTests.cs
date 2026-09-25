using CoffeePeek.Shared.Kernel;
using FluentAssertions;

namespace CoffeePeek.Shops.Application.Tests.Media;

public class MediaStorageUrlBuilderTests
{
    private const string ProxyEndpoint = "https://media.coffeepeek.by/img";
    private const string Bucket = "coffeepeek.shops";

    [Fact]
    public void BuildVariantUrl_BuildsPresetsOnlyImgproxyUrl()
    {
        var url = MediaStorageUrlBuilder.BuildVariantUrl(ProxyEndpoint, Bucket, "shops/abc.jpg", "card");

        url.Should().Be("https://media.coffeepeek.by/img/_/card/plain/coffeepeek.shops/shops/abc.jpg");
    }

    [Fact]
    public void BuildVariantUrl_TrimsSlashes_NoDoubleSlashes()
    {
        var url = MediaStorageUrlBuilder.BuildVariantUrl(
            "https://media.coffeepeek.by/img/", "/coffeepeek.shops/", "/shops/abc.jpg", "card");

        url.Should().Be("https://media.coffeepeek.by/img/_/card/plain/coffeepeek.shops/shops/abc.jpg");
    }

    [Fact]
    public void BuildVariantUrl_PercentEscapesKeySegments_KeepsSlashes()
    {
        var url = MediaStorageUrlBuilder.BuildVariantUrl(ProxyEndpoint, Bucket, "shops/a@b c?.jpg", "thumb");

        url.Should().Be("https://media.coffeepeek.by/img/_/thumb/plain/coffeepeek.shops/shops/a%40b%20c%3F.jpg");
    }

    [Fact]
    public void BuildVariantUrl_GuidKey_IsUnchanged()
    {
        const string key = "shops/3f2504e0-4f89-11d3-9a0c-0305e82c3301.webp";

        var url = MediaStorageUrlBuilder.BuildVariantUrl(ProxyEndpoint, Bucket, key, "detail");

        url.Should().Be($"https://media.coffeepeek.by/img/_/detail/plain/coffeepeek.shops/{key}");
    }

    [Theory]
    [InlineData(null, Bucket, "shops/abc.jpg", "card")]
    [InlineData("", Bucket, "shops/abc.jpg", "card")]
    [InlineData("   ", Bucket, "shops/abc.jpg", "card")]
    [InlineData(ProxyEndpoint, null, "shops/abc.jpg", "card")]
    [InlineData(ProxyEndpoint, "", "shops/abc.jpg", "card")]
    [InlineData(ProxyEndpoint, "   ", "shops/abc.jpg", "card")]
    [InlineData(ProxyEndpoint, Bucket, null, "card")]
    [InlineData(ProxyEndpoint, Bucket, "", "card")]
    [InlineData(ProxyEndpoint, Bucket, "   ", "card")]
    [InlineData(ProxyEndpoint, Bucket, "shops/abc.jpg", null)]
    [InlineData(ProxyEndpoint, Bucket, "shops/abc.jpg", "")]
    [InlineData(ProxyEndpoint, Bucket, "shops/abc.jpg", "   ")]
    public void BuildVariantUrl_MissingArgument_ReturnsNull(
        string endpoint, string bucket, string key, string preset)
    {
        MediaStorageUrlBuilder.BuildVariantUrl(endpoint, bucket, key, preset).Should().BeNull();
    }

    [Fact]
    public void BuildPublicUrl_FormatIsUnchanged()
    {
        MediaStorageUrlBuilder.BuildPublicUrl("https://media.coffeepeek.by/", "/coffeepeek.shops/", "/shops/abc.jpg")
            .Should().Be("https://media.coffeepeek.by/coffeepeek.shops/shops/abc.jpg");
    }
}
