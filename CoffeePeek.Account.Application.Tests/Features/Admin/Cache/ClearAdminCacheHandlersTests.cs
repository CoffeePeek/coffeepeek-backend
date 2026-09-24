using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.Admin.Cache;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Admin.Cache;

public class ClearAdminCacheHandlersTests
{
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Theory]
    [InlineData("revoked-user:*")]  // would re-enable every revoked access token
    [InlineData("Revoked-User:*")]
    [InlineData("*:*")]             // glob in prefix reaches every namespace
    [InlineData("*user:*")]
    [InlineData("?evoked-user:*")]
    [InlineData("[r]evoked-user:*")]
    [InlineData("*")]
    [InlineData("shop")]
    [InlineData(":*")]
    public async Task ClearByPattern_RejectsUnsafePatterns(string pattern)
    {
        var response = await ClearAdminCacheByPatternHandler.Handle(
            new ClearAdminCacheByPatternCommand(pattern), _cacheMock.Object, _ct);

        response.IsSuccess.Should().BeFalse();
        _cacheMock.Verify(c => c.RemoveByPattern(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("shop:*")]
    [InlineData("user:123:*")]
    public async Task ClearByPattern_AllowsScopedPatterns(string pattern)
    {
        _cacheMock.Setup(c => c.RemoveByPattern(pattern, _ct)).ReturnsAsync(2);

        var response = await ClearAdminCacheByPatternHandler.Handle(
            new ClearAdminCacheByPatternCommand(pattern), _cacheMock.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data!.ClearedCount.Should().Be(2);
    }

    [Fact]
    public async Task ClearKey_RejectsRevocationMarker()
    {
        var response = await ClearAdminCacheKeyHandler.Handle(
            new ClearAdminCacheKeyCommand("revoked-user:3f2504e0-4f89-11d3-9a0c-0305e82c3301"), _cacheMock.Object, _ct);

        response.IsSuccess.Should().BeFalse();
        _cacheMock.Verify(c => c.RemoveAsync(It.IsAny<CacheKey>()), Times.Never);
    }
}
