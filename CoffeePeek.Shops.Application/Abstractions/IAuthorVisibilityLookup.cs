namespace CoffeePeek.Shops.Application.Abstractions;

public interface IAuthorVisibilityLookup
{
    Task<Guid[]> ExcludedUserIdsAsync(Guid? viewerId, CancellationToken ct);
}
