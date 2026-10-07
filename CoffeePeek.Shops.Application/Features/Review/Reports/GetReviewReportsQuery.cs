using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public record GetReviewReportsQuery(ReviewReportStatus? Status, int Page, int PageSize);
