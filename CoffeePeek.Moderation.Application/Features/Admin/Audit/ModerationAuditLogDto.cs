using System.Text.Json.Serialization;
using CoffeePeek.Moderation.Domain.Entities;

namespace CoffeePeek.Moderation.Application.Features.Admin.Audit;

public record ModerationAuditLogDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationAuditEntityType EntityType,
    Guid EntityId,
    string EntityName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationAuditAction Action,
    Guid ModeratorUserId,
    string? Comment,
    DateTime CreatedAtUtc);
