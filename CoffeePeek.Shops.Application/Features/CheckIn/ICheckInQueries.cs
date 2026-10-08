using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.CheckIns;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public interface ICheckInQueries
{
    Task<UnifiedCheckInDto?> GetUnifiedByIdAsync(Guid id, Guid? readerId, CancellationToken ct = default);
    Task<UnifiedCheckInDto?> GetModerationSnapshotAsync(Guid id, int revision, CancellationToken ct = default);
    Task<CheckInPage> GetUnifiedByUserAsync(Guid userId, int pageNumber, int pageSize,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<PublicCheckInPage> GetPublicAsync(Guid? readerId, Guid? cityId, Guid? shopId, Guid? authorId,
        int pageSize, string? cursor, CancellationToken ct = default);
    Task<(decimal AverageRating, int Count)> GetStatsByShopAsync(Guid shopId, CancellationToken ct = default);
    Task<UnifiedCheckInDto[]> GetByUserAndShopAsync(Guid userId, Guid shopId, int page = 1, int pageSize = 10, CancellationToken ct = default);
}
