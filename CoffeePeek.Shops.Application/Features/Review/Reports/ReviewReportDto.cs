using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public record ReviewReportDto(Guid Id, Guid? CheckInId, Guid ReportedByUserId, string Text,
    ReviewReportStatus Status, DateTime CreatedAtUtc, Guid? ResolvedByAdminId, DateTime? ResolvedAtUtc)
{
    public static ReviewReportDto From(ReviewReport report) => new(report.Id, report.CheckInId,
        report.ReportedByUserId, report.Text, report.Status, report.CreatedAtUtc,
        report.ResolvedByAdminId, report.ResolvedAtUtc);
}
