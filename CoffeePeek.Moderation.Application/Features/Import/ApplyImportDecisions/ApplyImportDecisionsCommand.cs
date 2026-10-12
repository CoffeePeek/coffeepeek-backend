using System.Text.Json;

namespace CoffeePeek.Moderation.Application.Features.Import.ApplyImportDecisions;

public record ApplyImportDecisionsCommand(
    Dictionary<string, string>? Decisions,
    JsonElement? Raw,
    bool OverrideClosed,
    Guid ReviewerUserId);
