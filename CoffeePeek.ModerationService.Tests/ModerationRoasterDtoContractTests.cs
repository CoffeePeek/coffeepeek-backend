using System.Text.Json;
using CoffeePeek.Moderation.Application.Mapper;
using CoffeePeek.Shared.Kernel.Options;
using FluentAssertions;
using ContractModerationStatus = CoffeePeek.Contract.Enums.ModerationStatus;
using ModerationRoaster = CoffeePeek.Moderation.Domain.Aggregates.ModerationRoaster;

namespace CoffeePeek.ModerationService.Tests;

public class ModerationRoasterDtoContractTests
{
    [Fact]
    public void ApprovedRoaster_MappingAndSerialization_ExposeModerationStatus()
    {
        var roaster = ModerationRoaster.Create("Test Roaster", Guid.NewGuid(), null);
        roaster.Approve();
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions());

        var dto = mapper.Map<CoffeePeek.Contract.Dtos.CoffeeShop.ModerationRoasterDto>(roaster);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        dto.ModerationStatus.Should().Be(ContractModerationStatus.Approved);
        json.Should().Contain("\"moderationStatus\":1");
    }
}
