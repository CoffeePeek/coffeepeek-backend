using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed class CreateSavedCollectionRequest
{
    [Required, StringLength(100, MinimumLength = 1)] public string Title { get; init; } = "";
    [StringLength(1000)] public string? Description { get; init; }
    public string Visibility { get; init; } = "private";
    public FavoriteCollectionSelection[] Items { get; init; } = [];
    public UploadedPhotoDto? CoverUpload { get; init; }
}
