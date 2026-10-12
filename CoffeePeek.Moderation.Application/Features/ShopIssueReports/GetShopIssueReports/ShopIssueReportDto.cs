using System.Text.Json.Serialization;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Moderation.Application.Features.ShopIssueReports.GetShopIssueReports;

public record ShopIssueReportDto(
    Guid Id,
    Guid ShopId,
    Guid ReportedByUserId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ShopIssueCategory Category,
    string? Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ShopIssueReportStatus Status,
    Guid? ReviewedBy,
    DateTime? ReviewedAt,
    DateTime CreatedAtUtc);
