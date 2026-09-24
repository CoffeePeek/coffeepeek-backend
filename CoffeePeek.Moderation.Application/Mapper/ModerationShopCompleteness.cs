using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Moderation.Application.Mapper;

/// <summary>
/// Mirrors the Shops DB function from migration AddCoffeeShopDataCompleteness so moderators see the
/// score a submission will get once approved. Reviews (10) and check-ins (10) can't exist before approval.
/// ponytail: weights duplicated from SQL; keep both in sync if scoring changes.
/// </summary>
public static class ModerationShopCompleteness
{
    public static int Calculate(ModerationShopDto shop)
    {
        var score = 0;
        if (shop.ShopPhotos is { Count: > 0 }) score += 15;
        if (shop.Menu is { } menu
            && (menu.Items.Any(i => i.Availability == MenuItemAvailability.Present) || menu.Photos.Count > 0))
            score += 15;
        if (shop.Schedules is { Count: > 0 }) score += 15;
        if (shop.RoasterIds is { Count: > 0 }) score += 5;
        if (shop.BrewMethodIds is { Count: > 0 }) score += 5;
        if (shop.CoffeeBeanIds is { Count: > 0 }) score += 5;
        if (shop.EquipmentIds is { Count: > 0 }) score += 5;
        if (!string.IsNullOrWhiteSpace(shop.ShopContact?.InstagramLink)) score += 5;
        if (!string.IsNullOrWhiteSpace(shop.ShopContact?.PhoneNumber)) score += 5;
        if (!string.IsNullOrWhiteSpace(shop.ShopContact?.SiteLink)) score += 5;
        return score;
    }
}
