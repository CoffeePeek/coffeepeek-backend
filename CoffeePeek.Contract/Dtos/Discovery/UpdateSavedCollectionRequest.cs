using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed class UpdateSavedCollectionRequest
{
    [Required, StringLength(100, MinimumLength = 1)] public string Title { get; init; } = "";
    [StringLength(1000)] public string? Description { get; init; }
    [Required] public string Visibility { get; init; } = "";
    [Range(1, long.MaxValue)] public long ExpectedRevision { get; init; }
    /// <summary>Null preserves the current cover; only this owner's new upload can replace it.</summary>
    public UploadedPhotoDto? CoverUpload { get; init; }
    public bool RemoveCover { get; init; }
}
