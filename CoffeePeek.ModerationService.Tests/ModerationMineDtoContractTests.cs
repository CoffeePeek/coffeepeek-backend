using System.Text.Json;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Application.Mapper;
using CoffeePeek.Shared.Kernel.Options;
using FluentAssertions;
using ContractModerationStatus = CoffeePeek.Contract.Enums.ModerationStatus;
using ModerationRoaster = CoffeePeek.Moderation.Domain.Aggregates.ModerationRoaster;
using ModerationShop = CoffeePeek.Moderation.Domain.Aggregates.ModerationShop;

namespace CoffeePeek.ModerationService.Tests;

/// <summary>
/// GET */mine exposes what happened to a user's submission: status, rejection reason and (shops only)
/// the published Shops-service id. These tests pin the mapping and the camelCase JSON names.
/// </summary>
public class ModerationMineDtoContractTests
{
    private const string RejectReason = "Photos do not show the coffee bar";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RejectedRoaster_MapsRejectedReasonAndStatus()
    {
        var roaster = ModerationRoaster.Create("Test Roaster", Guid.NewGuid(), null);
        roaster.Reject(RejectReason);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions());

        var dto = mapper.Map<ModerationRoasterDto>(roaster);
        var json = JsonSerializer.Serialize(dto, Json);

        dto.ModerationStatus.Should().Be(ContractModerationStatus.Rejected);
        dto.RejectedReason.Should().Be(RejectReason);
        json.Should().Contain("\"rejectedReason\":");
    }

    [Fact]
    public void UnpublishedShop_MapsPublishedShopIdToNull()
    {
        var shop = ModerationShop.Create("Test Cafe", Guid.NewGuid(), Guid.NewGuid(), null);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions());

        var dto = mapper.Map<ModerationShopDto>(shop);

        dto.PublishedShopId.Should().BeNull("ShopId stays Guid.Empty until the shop is published");
    }

    [Fact]
    public void PublishedShop_MapsPublishedShopId()
    {
        var shop = ModerationShop.Create("Test Cafe", Guid.NewGuid(), Guid.NewGuid(), null);
        var publishedId = Guid.NewGuid();
        shop.AddShopId(publishedId);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions());

        var dto = mapper.Map<ModerationShopDto>(shop);

        dto.PublishedShopId.Should().Be(publishedId);
    }

    [Fact]
    public void RejectedShop_MapsRejectedReason_AndSerializesCamelCaseFields()
    {
        var shop = ModerationShop.Create("Test Cafe", Guid.NewGuid(), Guid.NewGuid(), null);
        shop.Reject(RejectReason);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions());

        var dto = mapper.Map<ModerationShopDto>(shop);
        var json = JsonSerializer.Serialize(dto, Json);

        dto.ModerationStatus.Should().Be(ContractModerationStatus.Rejected);
        dto.RejectedReason.Should().Be(RejectReason);
        json.Should().Contain("\"rejectedReason\":");
        json.Should().Contain("\"publishedShopId\":");
    }
}
