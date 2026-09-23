using CoffeePeek.Moderation.Domain.Common.Enums;

namespace CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;

public interface IShopIssueReportRepository
{
    void Add(ShopIssueReport report);
    Task<ShopIssueReport?> GetById(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<ShopIssueReport> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        ShopIssueReportStatus? status,
        Guid? shopId,
        CancellationToken ct = default);
}
