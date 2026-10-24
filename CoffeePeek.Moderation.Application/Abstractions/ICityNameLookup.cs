namespace CoffeePeek.Moderation.Application.Abstractions;

public interface ICityNameLookup
{
    Task<string?> GetNameAsync(Guid cityId, CancellationToken ct);
}
