using System.Text.Json;

namespace CoffeePeek.Moderation.Application.Features.Import.IngestImportFile;

public record IngestImportFileCommand(JsonElement Body);
