using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

public sealed class AndroidRelease : Entity<Guid>
{
    public string Version { get; private set; } = null!;
    public int VersionCode { get; private set; }
    public string FileUrl { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public long FileSize { get; private set; }
    public string Sha256 { get; private set; } = null!;
    public DateTime ReleasedAt { get; private set; }
    public bool IsActive { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private AndroidRelease()
    {
    }

    private AndroidRelease(
        Guid id,
        string version,
        int versionCode,
        string fileUrl,
        string fileName,
        long fileSize,
        string sha256,
        DateTime releasedAt)
    {
        Id = id;
        Version = version;
        VersionCode = versionCode;
        FileUrl = fileUrl;
        FileName = fileName;
        FileSize = fileSize;
        Sha256 = sha256;
        ReleasedAt = releasedAt;
    }

    public static AndroidRelease Create(
        string version,
        int versionCode,
        string fileUrl,
        string fileName,
        long fileSize,
        string sha256,
        DateTime releasedAt)
    {
        ValidateVersion(version);
        ValidateVersionCode(versionCode);
        AppDistributionSettings.ValidateHttpsUrl(fileUrl.Trim(), "APK file URL");
        ValidateFileName(fileName);
        ValidateFileSize(fileSize);
        ValidateSha256(sha256);

        return new AndroidRelease(
            Guid.NewGuid(),
            version.Trim(),
            versionCode,
            fileUrl.Trim(),
            fileName.Trim(),
            fileSize,
            sha256.Trim().ToLowerInvariant(),
            DateTime.SpecifyKind(releasedAt, DateTimeKind.Utc));
    }

    public void Publish() => IsActive = true;

    public void Unpublish() => IsActive = false;

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new DomainException("Version is required.");

        if (version.Trim().Length > BusinessConstants.MaxAndroidReleaseVersionLength)
            throw new DomainException($"Version cannot be longer than {BusinessConstants.MaxAndroidReleaseVersionLength} characters.");
    }

    private static void ValidateVersionCode(int versionCode)
    {
        if (versionCode <= 0)
            throw new DomainException("Version code must be greater than zero.");
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new DomainException("File name is required.");

        if (fileName.Trim().Length > BusinessConstants.MaxAndroidReleaseFileNameLength)
            throw new DomainException($"File name cannot be longer than {BusinessConstants.MaxAndroidReleaseFileNameLength} characters.");
    }

    private static void ValidateFileSize(long fileSize)
    {
        if (fileSize <= 0)
            throw new DomainException("File size must be greater than zero.");
    }

    private static void ValidateSha256(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
            throw new DomainException("SHA256 hash is required.");

        var normalized = sha256.Trim();
        if (normalized.Length != BusinessConstants.Sha256HashLength || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new DomainException("SHA256 hash must be a 64-character hexadecimal value.");
    }
}
