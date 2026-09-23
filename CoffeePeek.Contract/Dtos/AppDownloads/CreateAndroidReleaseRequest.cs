#nullable enable

using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Contract.Dtos.AppDownloads;

public record CreateAndroidReleaseRequest(
    [Required]
    [MaxLength(32)]
    string Version,

    [Range(1, int.MaxValue)]
    int VersionCode,

    [Required]
    [MaxLength(2048)]
    string FileUrl,

    [Required]
    [MaxLength(255)]
    string FileName,

    [Range(1, long.MaxValue)]
    long FileSize,

    [Required]
    [StringLength(64, MinimumLength = 64)]
    string Sha256,

    DateTime? ReleasedAt);
