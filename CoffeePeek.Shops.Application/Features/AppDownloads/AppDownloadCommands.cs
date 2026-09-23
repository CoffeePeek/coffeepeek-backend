namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public record UpdateGooglePlayDownloadCommand(Guid UserId, string? Url, bool Enabled);

public record UpdateAppStoreDownloadCommand(Guid UserId, string? Url, bool Enabled);

public record UpdateAndroidApkChannelCommand(Guid UserId, bool Enabled);

public record CreateAndroidReleaseCommand(
    Guid UserId,
    string Version,
    int VersionCode,
    string FileUrl,
    string FileName,
    long FileSize,
    string Sha256,
    DateTime? ReleasedAt);

public record PublishAndroidReleaseCommand(Guid UserId, Guid Id);
