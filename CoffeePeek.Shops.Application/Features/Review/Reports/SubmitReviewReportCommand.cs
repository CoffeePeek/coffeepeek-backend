using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public record SubmitReviewReportCommand(Guid CheckInId, Guid UserId, string Text);
