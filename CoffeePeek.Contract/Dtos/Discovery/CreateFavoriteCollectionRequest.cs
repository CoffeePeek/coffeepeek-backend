using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed class CreateFavoriteCollectionRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Title { get; set; } = "";
    [Required, MinLength(1), MaxLength(100)]
    public FavoriteCollectionSelection[] Items { get; set; } = [];
}
