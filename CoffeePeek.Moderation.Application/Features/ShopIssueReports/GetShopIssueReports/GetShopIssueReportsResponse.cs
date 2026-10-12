namespace CoffeePeek.Moderation.Application.Features.ShopIssueReports.GetShopIssueReports;

public record GetShopIssueReportsResponse(
    IReadOnlyList<ShopIssueReportDto> Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);
