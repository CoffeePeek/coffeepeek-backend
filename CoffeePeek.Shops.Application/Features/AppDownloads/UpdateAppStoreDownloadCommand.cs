namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public record UpdateAppStoreDownloadCommand(Guid UserId, string? Url, bool Enabled);
