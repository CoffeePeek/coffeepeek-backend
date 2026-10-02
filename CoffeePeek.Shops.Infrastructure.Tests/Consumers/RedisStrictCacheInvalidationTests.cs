using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public class RedisStrictCacheInvalidationTests
{
    [Fact]
    public async Task StrictDelete_PropagatesRedisFailure()
    {
        var database = new Mock<IDatabase>();
        database.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "unavailable"));
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);

        var cache = new RedisService(multiplexer.Object, NullLogger<RedisService>.Instance);
        var action = () => ((IStrictCacheInvalidator)cache).RemoveAsync(
            CacheKey.Shop.PublicPlatformStats(), TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<RedisConnectionException>();
    }

    [Fact]
    public async Task StrictPatternDelete_WithoutServer_PropagatesFailure()
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(Mock.Of<IDatabase>());
        var cache = new RedisService(multiplexer.Object, NullLogger<RedisService>.Instance);

        var action = () => ((IStrictCacheInvalidator)cache).RemoveByPatternAsync(
            CacheKey.Shop.SearchPattern(), TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<RedisConnectionException>();
    }
}
