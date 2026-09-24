using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Mapper;
using FluentAssertions;

namespace CoffeePeek.Moderation.Application.Tests.Mapper;

public class ModerationShopCompletenessTests
{
    [Fact]
    public void Calculate_EmptySubmission_IsZero()
    {
        ModerationShopCompleteness.Calculate(new ModerationShopDto()).Should().Be(0);
    }

    [Fact]
    public void Calculate_FullSubmission_Is80_BecauseReviewsAndCheckInsCannotExistYet()
    {
        var shop = new ModerationShopDto
        {
            ShopPhotos = [new ShortPhotoMetadataDto()],
            Menu = new ShopMenuDto(null, null, "BYN", default, null, null, [], [new ShortPhotoMetadataDto()]),
            Schedules = [new ScheduleDto(DayOfWeek.Monday, false, null)],
            RoasterIds = [Guid.NewGuid()],
            BrewMethodIds = [Guid.NewGuid()],
            CoffeeBeanIds = [Guid.NewGuid()],
            EquipmentIds = [Guid.NewGuid()],
            ShopContact = new ShopContactDto { InstagramLink = "ig", PhoneNumber = "+375", SiteLink = "x.by", Email = "a@b.c" }
        };

        ModerationShopCompleteness.Calculate(shop).Should().Be(80);
    }

    [Fact]
    public void Calculate_MenuWithOnlyAbsentItems_DoesNotCount()
    {
        var item = new ShopMenuItemDto("latte", "Латте", "Latte", default, MenuItemAvailability.Absent, null, "BYN", null, default);
        var shop = new ModerationShopDto
        {
            Menu = new ShopMenuDto(null, null, "BYN", default, null, null, [item], []),
            ShopContact = new ShopContactDto { InstagramLink = "  " }
        };

        ModerationShopCompleteness.Calculate(shop).Should().Be(0);
    }
}
