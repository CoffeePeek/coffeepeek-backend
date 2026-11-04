using System.Text.Json.Serialization;

namespace CoffeePeek.Contract.Dtos.Shelf;

public sealed record SetShelfProfileVisibilityRequest
{
    [JsonRequired] public bool IsVisible { get; init; }
}
