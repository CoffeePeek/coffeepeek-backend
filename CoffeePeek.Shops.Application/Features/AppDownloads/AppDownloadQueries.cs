namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public record GetPublicAppDownloadsQuery;

public record GetAdminAppDownloadsQuery;

public record GetAndroidReleasesQuery;

public record GetAndroidReleaseByIdQuery(Guid Id);

public record GetAppVersionQuery(string? Platform, string? Channel = null);

public record GetAppDownloadRedirectQuery(string Channel, string? UserAgent, string? Referer, string? Country);
