using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.ShopIssueReports.GetShopIssueReports;

public static class GetShopIssueReportsHandler
{
    public static async Task<Response<GetShopIssueReportsResponse>> Handle(
        GetShopIssueReportsQuery query,
        IShopIssueReportRepository repository,
        CancellationToken ct)
    {
        var domainStatus = query.Status.HasValue
            ? (ShopIssueReportStatus?)query.Status.Value
            : null;

        var (items, totalCount) = await repository.GetPagedAsync(
            query.Page,
            query.PageSize,
            domainStatus,
            query.ShopId,
            ct);

        var dtos = items
            .Select(x => new ShopIssueReportDto(
                x.Id,
                x.ShopId,
                x.ReportedByUserId,
                (Contract.Enums.ShopIssueCategory)x.Category,
                x.Description,
                (Contract.Enums.ShopIssueReportStatus)x.Status,
                x.ReviewedBy,
                x.ReviewedAt,
                x.CreatedAtUtc))
            .ToList();

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);

        return Response<GetShopIssueReportsResponse>.Success(new GetShopIssueReportsResponse(
            dtos, totalCount, totalPages, query.Page, query.PageSize));
    }
}
