using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Moderation.Application.Features.ShopIssueReports.GetShopIssueReports;

public record GetShopIssueReportsQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 20,
    ShopIssueReportStatus? Status = null,
    Guid? ShopId = null);
