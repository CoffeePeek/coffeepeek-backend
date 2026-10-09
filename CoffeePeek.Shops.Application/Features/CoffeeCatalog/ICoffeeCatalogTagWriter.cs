using CoffeePeek.Contract.Dtos.Coffee;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface ICoffeeCatalogTagWriter
{
    Task<RoasterTagDto> CreateTagAsync(CreateRoasterTagRequest request, CancellationToken ct);
    Task<RoasterTagDto?> EditTagAsync(Guid id, RoasterTagEdit request, CancellationToken ct);
    Task SetRoasterTagsAsync(Guid roasterId, Guid[] tagIds, Guid actorId, CancellationToken ct);
}
