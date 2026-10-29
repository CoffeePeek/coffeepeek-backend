namespace CoffeePeek.Contract.Dtos.Shelf;

public sealed record RestoreShelfBagRequest
{
    /// <summary>Clamped to 0…current shelf size; defaults to 0.</summary>
    public int? Position { get; init; }
}
