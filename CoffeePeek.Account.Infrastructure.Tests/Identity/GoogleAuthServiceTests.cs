using CoffeePeek.Account.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CoffeePeek.Account.Infrastructure.Tests.Identity;

public class GoogleAuthServiceTests
{
    private static GoogleAuthService CreateSut(string clientId) =>
        new(Microsoft.Extensions.Options.Options.Create(new OAuthGoogleOptions { ClientId = clientId }),
            NullLogger<GoogleAuthService>.Instance);

    [Fact]
    public async Task ValidateIdTokenAsync_WhenClientIdMissing_ReturnsNull()
    {
        var sut = CreateSut("");
        var result = await sut.ValidateIdTokenAsync("test", TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateIdTokenAsync_WhenTokenIsGarbage_ReturnsNull()
    {
        var sut = CreateSut("770099870632-5eeebr3eqdrma4reson9mv02j4lro583.apps.googleusercontent.com");
        var result = await sut.ValidateIdTokenAsync("test", TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }
}
