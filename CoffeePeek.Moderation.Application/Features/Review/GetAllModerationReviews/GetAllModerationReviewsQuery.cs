using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Moderation.Application.Features.Review.GetAllModerationReviews;

public record GetAllModerationReviewsQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 20,
    ModerationStatus? Status = null,
    [StringLength(100)] string? Search = null,
    Guid? UserId = null);
