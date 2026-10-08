using CoffeePeek.Contract.Dtos.CoffeeShop;

namespace CoffeePeek.Moderation.Application.Features.Review.GetAllModerationReviews;

public record GetAllModerationReviewsResponse(
    ModerationReviewDto[] Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);
