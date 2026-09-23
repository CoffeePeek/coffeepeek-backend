#nullable enable

namespace CoffeePeek.Contract.Dtos.AppDownloads;

public record AppDownloadsDto(
    AndroidDownloadsDto Android,
    IosDownloadsDto Ios);

public record AndroidDownloadsDto(
    DownloadChannelDto GooglePlay,
    ApkDownloadChannelDto Apk);

public record IosDownloadsDto(
    DownloadChannelDto AppStore);

public record DownloadChannelDto(
    bool Enabled,
    bool Available,
    string Url);

public record ApkDownloadChannelDto(
    bool Enabled,
    bool Available,
    string Url,
    string? Version,
    int? VersionCode,
    string? FileName,
    long? FileSize,
    string? Sha256,
    DateTime? ReleasedAt);
