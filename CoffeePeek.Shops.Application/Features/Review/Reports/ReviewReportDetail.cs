using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public record ReviewReportDetail(ReviewReportDto Report, ReportedCheckInDto? CheckIn);
