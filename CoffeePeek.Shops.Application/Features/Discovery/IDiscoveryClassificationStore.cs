using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public interface IDiscoveryClassificationStore
{
    Task<FilterGroup[]> PublicValuesAsync(CancellationToken ct);
    Task<AdminCoffeeFilterValue[]> AdminValuesAsync(CancellationToken ct);
    Task<AdminCoffeeFilterValue> CreateValueAsync(CreateCoffeeFilterValueRequest request, CancellationToken ct);
    Task<AdminCoffeeFilterValue?> EditValueAsync(Guid id, EditCoffeeFilterValueRequest request, CancellationToken ct);
    Task<AdminCoffeeFilterValue?> DeactivateValueAsync(Guid id, CancellationToken ct);
    Task<AdminCoffeeClassification?> GetClassificationAsync(Guid id, CancellationToken ct);
    Task<AdminCoffeeClassification?> EditClassificationAsync(Guid id, CoffeeClassificationEditRequest request, CancellationToken ct);
    Task<AdminCoffeeClassification?> EditVariantClassificationAsync(Guid id, Guid variantId, VariantClassificationEditRequest request, CancellationToken ct);
}
