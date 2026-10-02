namespace CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

public interface IReviewReportRepository
{
    void Add(ReviewReport report);
    Task<ReviewReport?> GetByIdAsync(Guid reportId, CancellationToken ct);
    Task<(ReviewReport[] Items, int TotalCount)> GetPageAsync(
        ReviewReportStatus? status, int page, int pageSize, CancellationToken ct);
}
