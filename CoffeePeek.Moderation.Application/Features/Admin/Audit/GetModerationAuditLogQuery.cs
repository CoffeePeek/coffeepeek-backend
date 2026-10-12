using System.Text.Json.Serialization;
using CoffeePeek.Moderation.Domain.Entities;

namespace CoffeePeek.Moderation.Application.Features.Admin.Audit;

public record GetModerationAuditLogQuery(
    int Page = 1,
    int PageSize = 20,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationAuditEntityType? EntityType = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationAuditAction? Action = null);
