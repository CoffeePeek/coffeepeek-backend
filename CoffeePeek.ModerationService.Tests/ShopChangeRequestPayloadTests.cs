using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.ShopChangeRequests;
using FluentAssertions;

namespace CoffeePeek.ModerationService.Tests;

public class ShopChangeRequestPayloadTests
{
    public static TheoryData<ShopChangeSection, ShopChangePayloadDto> ValidSections => new()
    {
        { ShopChangeSection.Description, new ShopChangePayloadDto(Description: "New description") },
        { ShopChangeSection.Contacts, new ShopChangePayloadDto(Contacts: new(null, null, null, "@coffee")) },
        { ShopChangeSection.Photos, new ShopChangePayloadDto(Photos: new([], [Photo()])) },
        { ShopChangeSection.Tags, new ShopChangePayloadDto(TagIds: [Guid.NewGuid()]) },
        { ShopChangeSection.Roasters, new ShopChangePayloadDto(RoasterIds: []) },
        { ShopChangeSection.Equipment, new ShopChangePayloadDto(EquipmentIds: [Guid.NewGuid()]) },
        { ShopChangeSection.Menu, new ShopChangePayloadDto(Menu: new(
            [new UpdateShopMenuItemRequest("espresso", MenuItemAvailability.Present, 5, 30)], [], [Photo()], 0)) },
        { ShopChangeSection.BrewMethods, new ShopChangePayloadDto(BrewMethodIds: [Guid.NewGuid()]) }
    };

    [Theory]
    [MemberData(nameof(ValidSections))]
    public void Serialize_AcceptsEverySupportedSection(ShopChangeSection section, ShopChangePayloadDto payload)
    {
        var json = ShopChangeRequestPayload.Serialize(section, payload);

        ShopChangeRequestPayload.Deserialize(json).Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Serialize_RejectsFieldsFromAnotherSection()
    {
        var payload = new ShopChangePayloadDto(
            Description: "Text",
            TagIds: [Guid.NewGuid()]);

        var act = () => ShopChangeRequestPayload.Serialize(ShopChangeSection.Description, payload);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Serialize_RejectsDescriptionInsideAnotherSection()
    {
        var payload = new ShopChangePayloadDto(
            Description: "Unrelated text",
            Contacts: new(null, null, null, "@coffee"));

        var act = () => ShopChangeRequestPayload.Serialize(ShopChangeSection.Contacts, payload);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Serialize_RejectsDuplicateRetainedPhotos()
    {
        var id = Guid.NewGuid();
        var payload = new ShopChangePayloadDto(Photos: new([id, id], []));

        var act = () => ShopChangeRequestPayload.Serialize(ShopChangeSection.Photos, payload);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Serialize_RejectsMenuWithoutRevision()
    {
        var payload = new ShopChangePayloadDto(Menu: new([], [], []));

        var act = () => ShopChangeRequestPayload.Serialize(ShopChangeSection.Menu, payload);

        act.Should().Throw<ArgumentException>().WithMessage("*revision*");
    }

    private static UploadedPhotoDto Photo() => new("photo.jpg", "image/jpeg", Guid.NewGuid().ToString(), 42);
}
