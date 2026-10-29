namespace CoffeePeek.Contract.Dtos.Shelf;

public sealed record FinishShelfBagRequest
{
    /// <summary>1–5, optional.</summary>
    public int? Rating { get; init; }
    public bool? BuyAgain { get; init; }
}
