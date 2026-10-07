using System.Text.Json.Nodes;

namespace CoffeePeek.LegacyCheckInMigration;

public sealed record LegacyCheckIn(Guid? VisitId, Guid? ReviewId, Guid? ModerationId, JsonObject CheckIn, JsonObject? Snapshot);
