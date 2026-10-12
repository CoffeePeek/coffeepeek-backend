namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public sealed record RecordAppDownloadRedirectCommand(
    Guid EventId,
    string Channel,
    string? UserAgent,
    string? Referer,
    string? Country);
