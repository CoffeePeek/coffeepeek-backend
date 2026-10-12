using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public static class CoffeeClassificationHandler
{
    public static Task<AdminCoffeeFilterValue> Handle(CreateCoffeeFilterValueCommand command, IDiscoveryClassificationStore store, CancellationToken ct)
        => store.CreateValueAsync(command.Request, ct);

    public static Task<AdminCoffeeFilterValue?> Handle(EditCoffeeFilterValueCommand command, IDiscoveryClassificationStore store, CancellationToken ct)
        => store.EditValueAsync(command.Id, command.Request, ct);

    public static Task<AdminCoffeeFilterValue?> Handle(DeactivateCoffeeFilterValueCommand command, IDiscoveryClassificationStore store, CancellationToken ct)
        => store.DeactivateValueAsync(command.Id, ct);

    public static Task<AdminCoffeeClassification?> Handle(EditCoffeeClassificationCommand command, IDiscoveryClassificationStore store, CancellationToken ct)
        => store.EditClassificationAsync(command.Id, command.Request, ct);

    public static Task<AdminCoffeeClassification?> Handle(EditCoffeeVariantClassificationCommand command, IDiscoveryClassificationStore store, CancellationToken ct)
        => store.EditVariantClassificationAsync(command.Id, command.VariantId, command.Request, ct);
}
