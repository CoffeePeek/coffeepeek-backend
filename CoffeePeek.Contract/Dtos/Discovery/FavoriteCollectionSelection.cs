using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed class FavoriteCollectionSelection
{
    [Required, RegularExpression("^(coffee_shop|roaster)$")]
    public string Kind { get; set; } = "";
    [Required, StringLength(160, MinimumLength = 1)]
    public string Slug { get; set; } = "";
}
