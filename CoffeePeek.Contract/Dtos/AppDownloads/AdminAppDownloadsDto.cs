#nullable enable

namespace CoffeePeek.Contract.Dtos.AppDownloads;

public record AdminAppDownloadsDto(
    AdminStoreChannelDto AndroidGooglePlay,
    AdminApkChannelDto AndroidApk,
    AdminStoreChannelDto IosAppStore,
    DateTime? UpdatedAt);

public record AdminStoreChannelDto(
    bool Enabled,
    bool Available,
    string PublicUrl,
    string? ExternalUrl);

public record AdminApkChannelDto(
    bool Enabled,
    bool Available,
    string PublicUrl,
    AndroidReleaseDto? ActiveRelease);

public record AndroidReleaseDto(
    Guid Id,
    string Version,
    int VersionCode,
    string FileUrl,
    string FileName,
    long FileSize,
    string Sha256,
    DateTime ReleasedAt,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
