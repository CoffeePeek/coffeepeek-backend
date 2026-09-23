using CoffeePeek.Gateway.Authentication;
using FluentAssertions;

namespace CoffeePeek.Gateway.Tests;

public class AppReleaseAutomationTokenValidatorTests
{
    private const string ValidToken = "test-token-that-is-at-least-32-characters-long";

    [Fact]
    public void IsValid_ReturnsTrue_ForMatchingStrongToken()
    {
        AppReleaseAutomationTokenValidator.IsValid(ValidToken, ValidToken).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, ValidToken)]
    [InlineData("", ValidToken)]
    [InlineData("short", "short")]
    [InlineData(ValidToken, null)]
    [InlineData(ValidToken, "different-token-that-is-at-least-32-characters")]
    public void IsValid_ReturnsFalse_ForMissingWeakOrDifferentToken(
        string? configuredToken,
        string? providedToken)
    {
        AppReleaseAutomationTokenValidator.IsValid(configuredToken, providedToken).Should().BeFalse();
    }
}
