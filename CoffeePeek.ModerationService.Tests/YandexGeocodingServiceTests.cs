using System.Globalization;
using System.Net;
using System.Text;
using CoffeePeek.Moderation.Infrastructure;
using CoffeePeek.Moderation.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ModerationService.Tests;

public class YandexGeocodingServiceTests
{
    private const string Body = """
        {"response":{"GeoObjectCollection":{"featureMember":[{"GeoObject":{"Point":{"pos":"27.561481 53.902496"}}}]}}}
        """;

    [Fact]
    public async Task GeocodeAsync_ParsesCoordinates_UnderCommaDecimalCulture()
    {
        // Regression: current-culture decimal parsing returned null on ru-RU hosts, so every address was "unverified".
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            var http = new HttpClient(new StubHandler(Body)) { BaseAddress = new Uri("https://geocode.test/") };
            var service = new YandexGeocodingService(
                http,
                Options.Create(new YandexApiOptions { ApiKey = "k" }),
                NullLogger<YandexGeocodingService>.Instance);

            var result = await service.GeocodeAsync("Minsk", TestContext.Current.CancellationToken);

            result.Should().NotBeNull();
            result!.Latitude.Should().Be(53.902496m);
            result.Longitude.Should().Be(27.561481m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
