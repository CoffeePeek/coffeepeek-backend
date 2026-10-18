namespace CoffeePeek.MediaService.Services.Backfill;

internal sealed record PhotoBackfillContent(string Status, long? Bytes, string? Digest = null);
