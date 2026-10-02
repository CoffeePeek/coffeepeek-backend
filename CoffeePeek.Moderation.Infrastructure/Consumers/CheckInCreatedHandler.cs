using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Moderation.Application.Features.Review.SendReviewToModeration;

namespace CoffeePeek.Moderation.Infrastructure.Consumers;

public static class CheckInCreatedHandler
{
    public static SendReviewToModerationCommand Handle(CheckinCreatedEvent @event)
    {
        var review = @event.ReviewDto;

        return new SendReviewToModerationCommand(
            review.CoffeeShopId,
            review.Header,
            review.Comment,
            review.Rating,
            review.Photos)
        {
            DrinkSlug = review.DrinkSlug,
            CustomDrinkName = review.CustomDrinkName,
            TrustedDrink = review.DrinkSlug is null ? null : new CoffeePeek.Shared.Kernel.Drinks.DrinkSelection(
                review.DrinkSlug, review.CustomDrinkName, review.DrinkNameRu!, review.DrinkNameEn!),
            UserId = review.UserId,
            UserName = review.Username
        };
    }
}