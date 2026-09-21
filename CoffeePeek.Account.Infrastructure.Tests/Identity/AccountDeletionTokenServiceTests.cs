using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.Identity;

public class AccountDeletionTokenServiceTests
{
    [Fact]
    public void Generate_ShouldReturnOpaqueTokenAndOnlyDeterministicHash()
    {
        var service = CreateService("first-pepper-at-least-thirty-two-characters");

        var first = service.Generate();
        var second = service.Generate();

        first.Token.Should().NotBe(first.Hash);
        first.Token.Should().NotBe(second.Token);
        first.Hash.Should().HaveLength(64);
        service.Hash(first.Token).Should().Be(first.Hash);
    }

    [Fact]
    public void Hash_WithDifferentPepper_ShouldProduceDifferentHash()
    {
        var token = "opaque-token";

        var first = CreateService("first-pepper-at-least-thirty-two-characters").Hash(token);
        var second = CreateService("second-pepper-at-least-thirty-two-characters").Hash(token);

        first.Should().NotBe(second);
    }

    private static AccountDeletionTokenService CreateService(string pepper) =>
        new(Microsoft.Extensions.Options.Options.Create(
            new AccountDeletionOptions { TokenPepper = pepper }));
}
