using CoffeePeek.Moderation.Infrastructure;
using CoffeePeek.Moderation.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ModerationService.Tests;

public class TelegramModerationOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://relay.example/telegram/sendMessage")]
    public void RelayUrl_OptionalHttpsEndpointIsAccepted(string relayUrl)
    {
        using var provider = CreateProvider(relayUrl);
        Assert.Equal(relayUrl, provider.GetRequiredService<IOptions<TelegramModerationOptions>>().Value.RelayUrl);
    }

    [Theory]
    [InlineData("http://relay.example/telegram/sendMessage")]
    [InlineData("/telegram/sendMessage")]
    [InlineData("https://user:secret@relay.example/telegram/sendMessage")]
    [InlineData("https://relay.example/telegram/sendMessage?token=secret")]
    [InlineData("https://relay.example/telegram/sendMessage#secret")]
    public void RelayUrl_UnsafeEndpointIsRejectedWithoutExposingIt(string relayUrl)
    {
        using var provider = CreateProvider(relayUrl);
        var error = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<TelegramModerationOptions>>().Value);
        Assert.DoesNotContain(relayUrl, error.Message);
    }

    [Fact]
    public void TelegramClient_DoesNotFollowRedirectsToAnotherDestination()
    {
        using var provider = CreateProvider("https://relay.example/telegram/sendMessage");
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(nameof(TelegramModerationClient));
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;
        Assert.False(Assert.IsType<SocketsHttpHandler>(handler).AllowAutoRedirect);
    }

    private static ServiceProvider CreateProvider(string relayUrl)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["YandexApiOptions:ApiKey"] = "test",
                ["GooglePlaces:ApiKey"] = "test",
                ["TelegramModeration:BotToken"] = "123456:fake-test-token",
                ["TelegramModeration:ChatId"] = "-123",
                ["TelegramModeration:RelayUrl"] = relayUrl
            }).Build());
        services.AddInfrastructure();
        return services.BuildServiceProvider();
    }
}
