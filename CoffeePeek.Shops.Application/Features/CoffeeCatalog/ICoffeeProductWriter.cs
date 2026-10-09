using CoffeePeek.Contract.Dtos.Coffee;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface ICoffeeProductWriter
{
    Task<AdminCoffeeDto> CreateAsync(CreateCoffeeRequest request, CancellationToken ct);
    Task<AdminCoffeeDto?> EditAsync(Guid id, CoffeeEditRequest request, CancellationToken ct);
    Task<bool> ArchiveAsync(Guid id, CancellationToken ct);
}
