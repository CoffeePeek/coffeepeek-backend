namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public record CreateAndroidReleaseCommand(
    Guid UserId,
    string Version,
    int VersionCode,
    string FileUrl,
    string FileName,
    long FileSize,
    string Sha256,
    DateTime? ReleasedAt);
