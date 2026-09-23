using System.Text.Json.Serialization;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Moderation.Application.Features.ShopIssueReports.ChangeShopIssueReportStatus;

public record ChangeShopIssueReportStatusCommand(
    Guid ReportId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ShopIssueReportStatus Status)
{
    [JsonIgnore] public Guid UserId { get; init; }
}
