using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Domain;

namespace CoffeePeek.Moderation.Application.Features.ShopIssueReports.CreateShopIssueReport;

public record CreateShopIssueReportCommand(
    [Required] Guid ShopId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    [Required] ShopIssueCategory Category,
    [MaxLength(BusinessConstants.MaxShopIssueReportDescriptionLength)] string? Description)
{
    [JsonIgnore] public Guid UserId { get; init; }
}
