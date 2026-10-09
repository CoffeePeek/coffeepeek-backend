using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public record ReportedCheckInDto(Guid Id, Guid ShopId, Guid UserId, string Username,
    string Text, int? RatingPlace, int? RatingService, int? RatingCoffee, DateTime VisitedAt,
    DateTime? DeletedAtUtc, bool IsPubliclyVisible);
