using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public record ReviewReportPage(ReviewReportDto[] Items, int TotalCount, int Page, int PageSize);
