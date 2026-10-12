using System.Text.Json.Serialization;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public record DecideShopChangeRequestCommand(
    Guid RequestId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationStatus Status,
    string? Comment,
    [property: JsonIgnore] Guid ModeratorUserId);
