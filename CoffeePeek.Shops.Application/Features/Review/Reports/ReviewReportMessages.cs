using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public record SubmitReviewReportCommand(Guid ReviewId, Guid UserId, string Text);
public record GetReviewReportsQuery(ReviewReportStatus? Status, int Page, int PageSize);
public record GetReviewReportQuery(Guid ReportId);
public record ResolveReviewReportCommand(Guid ReportId, Guid AdminId, bool DeleteReview);

public record ReviewReportDto(Guid Id, Guid ReviewId, Guid ReportedByUserId, string Text,
    ReviewReportStatus Status, DateTime CreatedAtUtc, Guid? ResolvedByAdminId, DateTime? ResolvedAtUtc)
{
    public static ReviewReportDto From(ReviewReport report) => new(report.Id, report.ReviewId,
        report.ReportedByUserId, report.Text, report.Status, report.CreatedAtUtc,
        report.ResolvedByAdminId, report.ResolvedAtUtc);
}

public record ReviewReportPage(ReviewReportDto[] Items, int TotalCount, int Page, int PageSize);
public record ReportedReviewDto(Guid Id, Guid CoffeeShopId, Guid UserId, string UserName,
    string? Header, string Comment, int RatingPlace, int RatingService, int RatingCoffee,
    bool IsSoftDelete, bool IsRemovedByAdmin);
public record ReviewReportDetail(ReviewReportDto Report, ReportedReviewDto Review);
