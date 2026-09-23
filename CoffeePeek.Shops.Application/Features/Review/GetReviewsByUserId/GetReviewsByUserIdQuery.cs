using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Shops.Application.Features.Review.GetReviewsByUserId;

public record GetReviewsByUserIdQuery(
    Guid UserId,
    [Range(1, 10_000)] int PageNumber = 1,
    [Range(1, 100)] int PageSize = 10);