using CoffeePeek.Shared.Auth.Options;
using FluentAssertions;

namespace CoffeePeek.ModerationService.Tests;

public class GatewayAuthOptionsTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void DevelopmentSecret_IsOnlyAllowedInDevelopment(bool isDevelopment, bool expected)
    {
        var options = new GatewayAuthOptions { SecretKey = GatewayAuthOptions.DevelopmentSecretKey };

        GatewayAuthOptions.IsAllowedIn(options, isDevelopment).Should().Be(expected);
    }

    [Fact]
    public void CustomSecret_IsAllowedEverywhere()
    {
        var options = new GatewayAuthOptions { SecretKey = new string('x', 40) };

        GatewayAuthOptions.IsAllowedIn(options, isDevelopment: false).Should().BeTrue();
    }
}
