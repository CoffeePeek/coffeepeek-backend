using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeShop.GetShopsInBounds;

public class GetShopsInBoundsHandlerTests
{
    private readonly Mock<ICoffeeShopQueries> _shopQueriesMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly MapClusteringOptions _options = new();

    [Fact]
    public async Task Handle_WithValidQuery_ReturnsSuccessWithShops()
    {
        var shopId = Guid.NewGuid();
        var shops = new MapShopDto[]
        {
            new MapShopDto
            {
                Id = shopId,
                Title = "Test",
                Type = CoffeePeek.Contract.Enums.CoffeeShopType.Specialty
            }
        };
        var query = new GetShopsInBoundsQuery(55.0m, 37.0m, 55.5m, 37.5m);

        _shopQueriesMock.Setup(q => q.GetMap(query, _options, _ct))
            .ReturnsAsync(new GetShopsInBoundsResponse(shops));

        var result = await GetShopsInBoundsHandler.Handle(
            query, _shopQueriesMock.Object, Options.Create(_options), _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Shops.Should().HaveCount(1);
        result.Data.Shops[0].Id.Should().Be(shopId);
        result.Data.Shops[0].Type.Should().Be(CoffeePeek.Contract.Enums.CoffeeShopType.Specialty);
    }

    [Fact]
    public async Task Handle_WithNoShopsInBounds_ReturnsSuccessWithEmptyArray()
    {
        var query = new GetShopsInBoundsQuery(0m, 0m, 0.1m, 0.1m);

        _shopQueriesMock.Setup(q => q.GetMap(query, _options, _ct))
            .ReturnsAsync(new GetShopsInBoundsResponse([]));

        var result = await GetShopsInBoundsHandler.Handle(
            query, _shopQueriesMock.Object, Options.Create(_options), _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Shops.Should().BeEmpty();
    }

    [Fact]
    public void LegacyResponse_OmitsAdditiveZoomFields()
    {
        var json = JsonSerializer.Serialize(new GetShopsInBoundsResponse([]));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.TryGetProperty(nameof(GetShopsInBoundsResponse.Shops), out _).Should().BeTrue();
        root.TryGetProperty(nameof(GetShopsInBoundsResponse.Clusters), out _).Should().BeFalse();
        root.TryGetProperty(nameof(GetShopsInBoundsResponse.Zones), out _).Should().BeFalse();
        root.TryGetProperty(nameof(GetShopsInBoundsResponse.IsTruncated), out _).Should().BeFalse();
    }
}
