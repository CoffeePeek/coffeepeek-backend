#nullable enable

namespace CoffeePeek.Contract.Dtos.Shelf;

/// <summary>Every current OnShelf bag ID exactly once, first bag first.</summary>
public sealed record ReorderShelfRequest
{
    public Guid[]? BagIds { get; init; }
}
