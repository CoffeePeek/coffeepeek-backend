namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public record UpdateGooglePlayDownloadCommand(Guid UserId, string? Url, bool Enabled);
