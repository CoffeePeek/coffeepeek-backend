using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public record ResolveReviewReportCommand(Guid ReportId, Guid AdminId, bool DeleteCheckIn);
